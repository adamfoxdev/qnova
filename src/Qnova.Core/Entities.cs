using System.Numerics;

namespace Qnova.Core;

public sealed class Player
{
    public PlayerMove Move;
    public float Yaw, Pitch;
    public int Health = 100, MaxHealth = 100;
    public int Shells = 25, Nails = 100, Rockets = 10;
    public HashSet<WeaponId> Owned = new(Enum.GetValues<WeaponId>());
    public WeaponId Current = WeaponId.Shotgun;
    public float NextFire;
    public int Frags;

    public Player(World w, Vector3 spawn) { Move = new PlayerMove(w) { Position = spawn }; }

    public Vector3 Eye => Move.Position + new Vector3(0, MoveVars.EyeHeight, 0);
    public Vector3 Look => PlayerMove.LookDir(Yaw, Pitch);
    public bool Alive => Health > 0;

    public int Ammo(AmmoType t) => t switch { AmmoType.Shells => Shells, AmmoType.Nails => Nails, AmmoType.Rockets => Rockets, _ => int.MaxValue };

    public void Spend(AmmoType t, int n)
    {
        switch (t)
        {
            case AmmoType.Shells: Shells -= n; break;
            case AmmoType.Nails: Nails -= n; break;
            case AmmoType.Rockets: Rockets -= n; break;
        }
    }
}

/// <summary>A stationary damageable dummy that respawns.</summary>
public sealed class Target
{
    public Vector3 Origin;
    public Vector3 Velocity;
    public int Health = 100;
    public float RespawnAt;
    public Vector3 Half = new(16, 28, 16);
    public Aabb Bounds => Aabb.FromCenter(Origin, Half);
    public bool Alive => Health > 0;
}

public enum ProjectileKind { Nail, Grenade, Rocket }

public sealed class Projectile
{
    public ProjectileKind Kind;
    public Vector3 Pos, Vel;
    public float Expires;
    public int Damage;
    public float Splash;
}

public enum EventKind { Explosion, Impact, Tracer, Hurt, Kill, Shot, Bounce, DryFire }

/// <summary>Arg carries the (int)WeaponId for Shot and DryFire events.</summary>
public readonly record struct GameEvent(EventKind Kind, Vector3 A, Vector3 B = default, int Arg = 0);
