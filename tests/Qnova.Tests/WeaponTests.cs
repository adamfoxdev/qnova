using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class WeaponTests
{
    static GameWorld Arena1()
    {
        var w = new World();
        w.Add(new(-10000, -64, -10000), new(10000, 0, 10000));
        var g = new GameWorld(w, new Vector3(0, 28, 0));
        g.Tick(default, false); // settle
        return g;
    }

    static void Run(GameWorld g, int ticks, UserCmd cmd = default, bool fire = false)
    {
        for (int i = 0; i < ticks; i++) g.Tick(cmd, fire);
    }

    [Fact]
    public void Shotgun_fires_six_pellets_and_uses_one_shell()
    {
        var g = Arena1();
        g.Player.Current = WeaponId.Shotgun;
        int shells = g.Player.Shells;
        g.Tick(default, true);
        Assert.Equal(shells - 1, g.Player.Shells);
        Assert.Equal(6, g.Events.Count(e => e.Kind == EventKind.Tracer));
    }

    [Fact]
    public void Super_shotgun_fires_fourteen_pellets_for_two_shells()
    {
        var g = Arena1();
        g.Player.Current = WeaponId.SuperShotgun;
        int shells = g.Player.Shells;
        g.Tick(default, true);
        Assert.Equal(shells - 2, g.Player.Shells);
        Assert.Equal(14, g.Events.Count(e => e.Kind == EventKind.Tracer));
    }

    [Fact]
    public void Refire_rate_is_respected()
    {
        var g = Arena1();
        g.Player.Current = WeaponId.RocketLauncher;
        g.Player.Rockets = 10;
        Run(g, (int)(GameWorld.TickRate * 1.7f), fire: true);   // 0.8s refire -> shots at t=0, .8, 1.6
        Assert.Equal(7, g.Player.Rockets);
    }

    [Fact]
    public void Cannot_fire_without_ammo()
    {
        var g = Arena1();
        g.Player.Current = WeaponId.Nailgun; g.Player.Nails = 0;
        Run(g, 10, fire: true);
        Assert.Empty(g.Projectiles);
    }

    [Fact]
    public void Shotgun_kills_target_at_close_range()
    {
        var g = Arena1();
        g.Targets.Add(new Target { Origin = new Vector3(0, 28, -200) });
        g.Player.Current = WeaponId.SuperShotgun;
        g.Player.Shells = 20;
        Run(g, (int)(GameWorld.TickRate * 3), fire: true);   // 4 volleys of 14x4=56 max
        Assert.False(g.Targets[0].Alive);
        Assert.Equal(1, g.Player.Frags);
    }

    [Fact]
    public void Axe_only_reaches_64_units()
    {
        var g = Arena1();
        var t = new Target { Origin = new Vector3(0, 28, -100) };
        g.Targets.Add(t);
        g.Player.Current = WeaponId.Axe;
        g.Tick(default, true);
        Assert.Equal(100, t.Health);
        t.Origin = new Vector3(0, 28, -60);   // face at -76 from center... eye at z=0, box near face z=-44
        g.Player.NextFire = 0;
        g.Tick(default, true);
        Assert.Equal(80, t.Health);
    }

    [Fact]
    public void Nail_projectile_travels_and_damages()
    {
        var g = Arena1();
        var t = new Target { Origin = new Vector3(0, 28, -500) };
        g.Targets.Add(t);
        g.Player.Current = WeaponId.Nailgun;
        g.Tick(default, true);
        Run(g, (int)(GameWorld.TickRate * 0.7f));
        Assert.Equal(91, t.Health);
    }

    [Fact]
    public void Rocket_direct_hit_does_100_to_120_and_explodes_on_walls()
    {
        var g = Arena1();
        var t = new Target { Origin = new Vector3(0, 28, -500), Health = 500 };
        g.Targets.Add(t);
        g.Player.Current = WeaponId.RocketLauncher;
        g.Tick(default, true);
        Run(g, (int)(GameWorld.TickRate * 1.0f));
        Assert.InRange(500 - t.Health, 100, 120);
        Assert.Contains(g.Events, e => e.Kind == EventKind.Explosion);
        Assert.Empty(g.Projectiles);
    }

    [Fact]
    public void Rocket_splash_damages_nearby_target_with_falloff()
    {
        var g = Arena1();
        var t = new Target { Origin = new Vector3(60, 28, -300), Health = 500 };
        g.Targets.Add(t);
        g.Player.Current = WeaponId.RocketLauncher;
        g.Player.Pitch = 0;
        g.Tick(default, true);
        // Rocket flies straight down -Z and hits nothing until it expires far away; fire into the floor instead.
        g.Projectiles.Clear();
        g.RadiusDamage(new Vector3(60, 28, -300) + new Vector3(0, 0, 40), 160, 120, null);
        Assert.Equal(500 - (120 - 20), t.Health);   // 120 - 0.5*40 = 100
    }

    [Fact]
    public void Rocket_jump_launches_player_and_costs_health()
    {
        var g = Arena1();
        g.Player.Current = WeaponId.RocketLauncher;
        var down = new UserCmd { Pitch = -90 };    // look straight down
        g.Tick(down, true);
        for (int i = 0; i < 30; i++) g.Tick(down, false);
        Assert.True(g.Player.Move.Velocity.Y > 0 || g.Player.Move.Position.Y > 40, "rocket jump should launch the player");
        Assert.True(g.Player.Health < 100, "self damage");
        Assert.True(g.Player.Health > 40, "self damage is halved");
    }

    [Fact]
    public void Grenade_bounces_and_explodes_on_fuse()
    {
        var g = Arena1();
        g.Player.Current = WeaponId.GrenadeLauncher;
        g.Tick(default, true);
        Assert.Single(g.Projectiles);
        Run(g, (int)(GameWorld.TickRate * 1.5f));
        Assert.Single(g.Projectiles);   // still bouncing
        Run(g, (int)(GameWorld.TickRate * 1.2f));
        Assert.Empty(g.Projectiles);
        Assert.Contains(g.Events, e => e.Kind == EventKind.Explosion);
    }

    [Fact]
    public void Arena_builds_and_stairs_are_walkable()
    {
        var g = Arena.Build();
        var m = g.Player.Move;
        m.Position = new Vector3(200, 28, 0);
        var cmd = new UserCmd { Forward = 1, Yaw = -90 };   // yaw -90 faces +X
        for (int i = 0; i < 400; i++) g.Tick(cmd, false);
        Assert.InRange(m.Position.Y, 128 + 27, 128 + 29);   // on top of the platform
    }
}
