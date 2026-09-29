using System.Numerics;

namespace Qnova.Core;

/// <summary>Fixed-step simulation of one player, dummy targets and projectiles.</summary>
public sealed class GameWorld
{
    public const float TickRate = 72f;
    public const float Dt = 1f / TickRate;
    static readonly Vector3 NailHalf = new(1, 1, 1);
    static readonly Vector3 GrenadeHalf = new(2, 2, 2);

    public readonly World Map;
    public readonly MoveSettings Settings = new();
    public readonly Player Player;
    public readonly GameConsole Console = new();
    public readonly Vector3 SpawnPoint;
    public bool InfiniteAmmo;
    public readonly List<Target> Targets = new();
    public readonly List<Projectile> Projectiles = new();
    public readonly List<GameEvent> Events = new();
    public float Time;
    readonly Random _rng;

    public GameWorld(World map, Vector3 spawn, int seed = 1)
    {
        Map = map;
        SpawnPoint = spawn;
        Player = new Player(map, spawn, Settings);
        _rng = new Random(seed);
        GameCommands.Install(this);
    }

    public void Tick(UserCmd cmd, bool fire, WeaponId? select = null)
    {
        Time += Dt;
        var p = Player;
        p.Yaw = cmd.Yaw; p.Pitch = cmd.Pitch;
        if (select is { } w && p.Owned.Contains(w)) p.Current = w;

        if (p.Alive)
        {
            p.Move.Tick(cmd, Dt);
            if (fire && Time >= p.NextFire) Fire();
        }
        UpdateProjectiles();
        foreach (var t in Targets)
            if (!t.Alive && Time >= t.RespawnAt) t.Health = 100;
    }

    // ---- firing ----

    void Fire()
    {
        var p = Player;
        var def = WeaponDef.Get(p.Current);
        if (p.Ammo(def.Ammo) < def.AmmoPerShot)
        {
            p.NextFire = Time + 0.25f;   // rate-limit the empty click
            Events.Add(new GameEvent(EventKind.DryFire, p.Eye, Arg: (int)def.Id));
            return;
        }
        if (!InfiniteAmmo) p.Spend(def.Ammo, def.AmmoPerShot);
        p.NextFire = Time + def.Refire;
        Events.Add(new GameEvent(EventKind.Shot, p.Eye, Arg: (int)def.Id));

        var dir = p.Look;
        switch (def.Mode)
        {
            case FireMode.Melee:
                Hitscan(p.Eye, dir, def.Range, def.Damage, false);
                break;
            case FireMode.Hitscan:
                var right = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
                var up = Vector3.Cross(right, dir);
                for (int i = 0; i < def.Pellets; i++)
                {
                    var d = Vector3.Normalize(dir + right * (Crandom() * def.SpreadX) + up * (Crandom() * def.SpreadY));
                    Hitscan(p.Eye, d, def.Range, def.Damage, true);
                }
                break;
            case FireMode.Nail:
                // Nailguns alternate a small sideways offset in Q1; we keep a straight shot.
                Projectiles.Add(new Projectile { Kind = ProjectileKind.Nail, Pos = p.Eye, Vel = dir * def.ProjectileSpeed, Damage = def.Damage, Expires = Time + 6 });
                break;
            case FireMode.Rocket:
                Projectiles.Add(new Projectile { Kind = ProjectileKind.Rocket, Pos = p.Eye, Vel = dir * def.ProjectileSpeed, Damage = def.Damage, Splash = def.SplashRadius, Expires = Time + 5 });
                break;
            case FireMode.Grenade:
                // Q1: forward*600 + up*200 (+ right*10 kick), bounces, 2.5s fuse.
                var up2 = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY)), dir));
                Projectiles.Add(new Projectile { Kind = ProjectileKind.Grenade, Pos = p.Eye, Vel = dir * def.ProjectileSpeed + up2 * 200f, Damage = def.Damage, Splash = def.SplashRadius, Expires = Time + 2.5f });
                break;
        }
    }

    float Crandom() => (float)(_rng.NextDouble() * 2 - 1);

    void Hitscan(Vector3 origin, Vector3 dir, float range, int damage, bool tracer)
    {
        var wall = Map.TraceRay(origin, origin + dir * range);
        float best = wall.Fraction * range;
        Target? victim = null;
        foreach (var t in Targets)
        {
            if (!t.Alive) continue;
            if (World.RayVsBox(origin, dir * range, t.Bounds.Min, t.Bounds.Max, out float f, out _))
            {
                float dist = f * range;
                if (dist <= best) { best = dist; victim = t; }
            }
        }
        var hit = origin + dir * best;
        if (tracer) Events.Add(new GameEvent(EventKind.Tracer, origin, hit));
        if (victim != null) Damage(victim, damage, dir, Player);
        else if (wall.Hit) Events.Add(new GameEvent(EventKind.Impact, hit, wall.Normal));
    }

    // ---- projectiles ----

    void UpdateProjectiles()
    {
        for (int i = Projectiles.Count - 1; i >= 0; i--)
        {
            var pr = Projectiles[i];
            if (pr.Kind == ProjectileKind.Grenade) pr.Vel.Y -= Settings.Gravity * Dt;

            var end = pr.Pos + pr.Vel * Dt;
            var half = pr.Kind == ProjectileKind.Grenade ? GrenadeHalf : NailHalf;
            var wall = Map.TraceBox(pr.Pos, end, half);
            var segEnd = wall.EndPos;

            Target? hitT = FirstTargetHit(pr.Pos, segEnd);
            if (hitT != null)
            {
                Detonate(pr, hitT, pr.Pos);
                Projectiles.RemoveAt(i);
                continue;
            }

            pr.Pos = segEnd;
            if (wall.Hit)
            {
                if (pr.Kind == ProjectileKind.Grenade)
                {
                    pr.Vel = PlayerMove.Clip(pr.Vel, wall.Normal, 1.5f);
                    if (pr.Vel.LengthSquared() > 50f * 50f) Events.Add(new GameEvent(EventKind.Bounce, pr.Pos));
                    if (wall.Normal.Y > 0.7f) pr.Vel *= new Vector3(0.7f, 0.5f, 0.7f); // floor friction
                }
                else
                {
                    Detonate(pr, null, pr.Pos);
                    Projectiles.RemoveAt(i);
                    continue;
                }
            }
            if (Time >= pr.Expires)
            {
                if (pr.Kind == ProjectileKind.Nail) Events.Add(new GameEvent(EventKind.Impact, pr.Pos, Vector3.UnitY));
                else Detonate(pr, null, pr.Pos);
                Projectiles.RemoveAt(i);
            }
        }
    }

    Target? FirstTargetHit(Vector3 a, Vector3 b)
    {
        var d = b - a;
        float len = d.Length();
        Target? best = null; float bestT = float.MaxValue;
        foreach (var t in Targets)
        {
            if (!t.Alive) continue;
            var box = t.Bounds;
            bool inside = box.Overlaps(new Aabb(a - NailHalf, a + NailHalf));
            if (inside) return t;
            if (len > 1e-6f && World.RayVsBox(a, d, box.Min - NailHalf, box.Max + NailHalf, out float f, out _) && f < bestT)
            { bestT = f; best = t; }
        }
        return best;
    }

    void Detonate(Projectile pr, Target? direct, Vector3 at)
    {
        var dir = pr.Vel.LengthSquared() > 0 ? Vector3.Normalize(pr.Vel) : Vector3.UnitY;
        if (pr.Kind == ProjectileKind.Nail)
        {
            if (direct != null) Damage(direct, pr.Damage, dir, Player);
            Events.Add(new GameEvent(EventKind.Impact, at, -dir));
            return;
        }
        // Rocket direct hits deal 100-120 damage on the entity struck (Q1: 100 + rand(0..20)).
        if (direct != null && pr.Kind == ProjectileKind.Rocket)
            Damage(direct, 100 + (int)(_rng.NextDouble() * 20), dir, Player, knock: false);
        RadiusDamage(at, pr.Splash, pr.Damage, direct);
        Events.Add(new GameEvent(EventKind.Explosion, at));
    }

    /// <summary>Q1 T_RadiusDamage: points = dmg - 0.5 * distance(origin, target center); self damage halved.</summary>
    public void RadiusDamage(Vector3 at, float radius, int damage, Target? ignore)
    {
        var p = Player;
        if (p.Alive)
        {
            float pts = damage - 0.5f * Vector3.Distance(at, p.Move.Position);
            if (pts > 0 && Visible(at, p.Move.Position))
            {
                pts *= 0.5f;
                Knock(ref p.Move.Velocity, p.Move.Position - at, pts);
                if (p.Move.Velocity.Y > 0) p.Move.OnGround = false;
                if (!p.God) p.Health -= (int)pts;
                Events.Add(new GameEvent(EventKind.Hurt, at));
            }
        }
        foreach (var t in Targets)
        {
            if (!t.Alive || t == ignore) continue;
            float pts = damage - 0.5f * Vector3.Distance(at, t.Origin);
            if (pts > 0 && Visible(at, t.Origin))
                Damage(t, (int)pts, Vector3.Normalize(t.Origin - at), p);
        }
    }

    bool Visible(Vector3 a, Vector3 b) => !Map.TraceRay(a, b).Hit;

    static void Knock(ref Vector3 v, Vector3 dir, float damage)
    {
        if (dir.LengthSquared() < 1e-6f) return;
        v += Vector3.Normalize(dir) * (damage * 8f);
    }

    void Damage(Target t, int dmg, Vector3 dir, Player by, bool knock = true)
    {
        if (!t.Alive) return;
        t.Health -= dmg;
        if (knock) t.Velocity += dir * dmg * 8f;
        Events.Add(new GameEvent(EventKind.Hurt, t.Origin));
        if (t.Health <= 0)
        {
            by.Frags++;
            t.RespawnAt = Time + 3f;
            Events.Add(new GameEvent(EventKind.Kill, t.Origin));
        }
    }
}

public static class GameWorldExtensions
{
    /// <summary>Full restore: position, health, ammo, and target dummies.</summary>
    public static void Respawn(this GameWorld g)
    {
        var p = g.Player;
        p.Move.Position = g.SpawnPoint; p.Move.Velocity = default; p.Move.OnGround = false;
        p.Health = p.MaxHealth; p.Shells = 25; p.Nails = 100; p.Rockets = 10;
        g.Projectiles.Clear();
        foreach (var t in g.Targets) { t.Health = 100; t.Velocity = default; }
    }
}
