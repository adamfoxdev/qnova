using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class PickupTests
{
    static GameWorld Flat()
    {
        var w = new World();
        w.Add(new(-3000, -64, -3000), new(3000, 0, 3000));
        var g = new GameWorld(w, new Vector3(0, 28, 0), seed: 3);
        g.SpawnPoints.Add(new Vector3(0, 28, 0));
        g.Tick(default, false);
        return g;
    }

    static Pickup Add(GameWorld g, PickupKind kind, int amount = 25, WeaponId w = WeaponId.Axe, float x = 0, float z = 0)
    {
        var k = new Pickup { Kind = kind, Amount = amount, Weapon = w, Position = new Vector3(x, 16, z) };
        g.Pickups.Add(k);
        return k;
    }

    static void Sim(GameWorld g, float seconds)
    {
        for (int i = 0; i < (int)(seconds * GameWorld.TickRate); i++) g.Tick(default, false);
    }

    [Fact]
    public void Health_pickup_heals_capped_and_starts_a_30s_cooldown()
    {
        var g = Flat();
        g.Player.Health = 60;
        var k = Add(g, PickupKind.Health, 25);
        g.Tick(default, false);
        Assert.Equal(85, g.Player.Health);
        Assert.False(k.Active);
        Assert.Contains(g.Events, e => e.Kind == EventKind.Pickup && e.B.X == 1);
        Assert.Contains(g.Console.Lines, l => l.Contains("25 health"));

        // Not back at 29s, back by 30.1s. (Full health, so standing on it doesn't instantly re-collect it.)
        g.Player.Health = 100;
        Sim(g, 29f);
        Assert.False(k.Active);
        Sim(g, 1.1f);
        Assert.True(k.Active);
        Assert.Contains(g.Events, e => e.Kind == EventKind.ItemRespawn);
        // ...and is collectable again as soon as we need it.
        g.Player.Health = 70;
        g.Tick(default, false);
        Assert.Equal(95, g.Player.Health);
        Assert.False(k.Active);
    }

    [Fact]
    public void Cooldown_is_exactly_the_configured_time()
    {
        var g = Flat();
        g.Player.Health = 10;
        var k = Add(g, PickupKind.Health, 5);
        g.Tick(default, false);
        float taken = g.Time;
        Assert.InRange(k.RespawnAt - taken, 29.99f, 30.01f);

        g.Console.Execute("sv_pickup_respawn 5");
        g.Player.Health = 10; k.Active = true;
        g.Tick(default, false);
        Assert.InRange(k.RespawnAt - g.Time, 4.99f, 5.01f);
    }

    [Fact]
    public void Full_health_leaves_the_item_in_place()
    {
        var g = Flat();
        var k = Add(g, PickupKind.Health);
        Sim(g, 1);
        Assert.True(k.Active);
        Assert.Equal(100, g.Player.Health);
    }

    [Fact]
    public void Ammo_is_capped_and_rejected_when_full()
    {
        var g = Flat();
        g.Player.Shells = 90;
        var k = Add(g, PickupKind.Shells, 20);
        g.Tick(default, false);
        Assert.Equal(Player.MaxShells, g.Player.Shells);   // took only 10
        Assert.False(k.Active);

        Sim(g, 30.5f);                                      // respawns, but we're full now
        Assert.True(k.Active);
        Sim(g, 1);
        Assert.True(k.Active);
    }

    [Fact]
    public void Weapon_pickup_grants_gun_and_ammo_and_switches_up()
    {
        var g = Flat();
        Assert.DoesNotContain(WeaponId.RocketLauncher, g.Player.Owned);
        g.Player.Rockets = 0;
        Add(g, PickupKind.Weapon, 0, WeaponId.RocketLauncher);
        g.Tick(default, false);
        Assert.Contains(WeaponId.RocketLauncher, g.Player.Owned);
        Assert.Equal(5, g.Player.Rockets);
        Assert.Equal(WeaponId.RocketLauncher, g.Player.Current);
    }

    [Fact]
    public void Owned_weapon_pickup_still_gives_ammo_but_not_when_full()
    {
        var g = Flat();
        g.Player.Owned.Add(WeaponId.Nailgun); g.Player.Nails = 50;
        var k = Add(g, PickupKind.Weapon, 0, WeaponId.Nailgun);
        g.Tick(default, false);
        Assert.Equal(80, g.Player.Nails);
        Assert.False(k.Active);
        g.Player.Nails = Player.MaxNails; k.Active = true;
        Sim(g, 1);
        Assert.True(k.Active);
    }

    [Fact]
    public void Dead_players_do_not_collect()
    {
        var g = Flat();
        g.Player.Health = 10;
        g.Console.Execute("kill");
        var k = Add(g, PickupKind.Health);
        Sim(g, 1);
        Assert.True(k.Active);
    }

    [Fact]
    public void Human_loses_extra_guns_on_death_and_respawns_with_axe_and_shotgun()
    {
        var g = Flat();
        Add(g, PickupKind.Weapon, 0, WeaponId.SuperShotgun);
        g.Tick(default, false);
        Assert.Contains(WeaponId.SuperShotgun, g.Player.Owned);
        g.Console.Execute("kill");
        Sim(g, 3.3f);
        Assert.True(g.Player.Alive);
        Assert.Equal(new[] { WeaponId.Axe, WeaponId.Shotgun }, g.Player.Owned.OrderBy(x => x));
        Assert.Equal(WeaponId.Shotgun, g.Player.Current);
    }

    [Fact]
    public void Weapon_console_command_requires_ownership()
    {
        var g = Flat();
        g.Console.Execute("weapon rl");
        Assert.Equal(WeaponId.Shotgun, g.Player.Current);
        Assert.Contains(g.Console.Lines, l => l.Contains("don't have"));
        g.Console.Execute("weapon axe");
        Assert.Equal(WeaponId.Axe, g.Player.Current);
    }

    [Fact]
    public void Bots_pick_things_up_too_without_the_human_being_told()
    {
        var g = Flat();
        var bot = g.AddBot();
        bot.Body.Health = 40;
        bot.Body.Move.Position = new Vector3(500, 28, 500);
        g.Console.Execute("bot_ai 0", echo: false);
        var k = Add(g, PickupKind.Health, 25, x: 500, z: 500);
        g.Tick(default, false);
        Assert.Equal(65, bot.Body.Health);
        Assert.False(k.Active);
        Assert.DoesNotContain(g.Console.Lines, l => l.Contains("You got"));
        Assert.Contains(g.Events, e => e.Kind == EventKind.Pickup && e.B.X == 0);
    }

    [Fact]
    public void Pickups_command_lists_state_and_respawn_resets_them()
    {
        var g = Flat();
        g.Player.Health = 50;
        var k = Add(g, PickupKind.Health);
        g.Tick(default, false);
        g.Console.Execute("pickups");
        Assert.Contains(g.Console.Lines, l => l.Contains("25 health") && l.Contains("back in"));
        g.Console.Execute("respawn");
        Assert.True(k.Active);
    }

    [Fact]
    public void Arena_has_a_sensible_set_of_valid_pickups()
    {
        var g = Arena.Build(bots: 0);
        Assert.Equal(5, g.Pickups.Count(k => k.Kind == PickupKind.Weapon));
        Assert.Equal(
            new[] { WeaponId.SuperShotgun, WeaponId.Nailgun, WeaponId.SuperNailgun, WeaponId.GrenadeLauncher, WeaponId.RocketLauncher }.OrderBy(x => x),
            g.Pickups.Where(k => k.Kind == PickupKind.Weapon).Select(k => k.Weapon).OrderBy(x => x));
        Assert.True(g.Pickups.Count(k => k.Kind == PickupKind.Health) >= 5);
        Assert.True(g.Pickups.Count(k => k.Kind == PickupKind.Shells) >= 2);
        Assert.True(g.Pickups.Count(k => k.Kind == PickupKind.Nails) >= 2);
        Assert.True(g.Pickups.Count(k => k.Kind == PickupKind.Rockets) >= 2);
        foreach (var k in g.Pickups)
        {
            Assert.True(g.Map.IsEmpty(k.Position, Pickup.Half), $"{k.Name} at {k.Position} is inside geometry");
            var below = g.Map.TraceBox(k.Position, k.Position - new Vector3(0, 8, 0), Pickup.Half);
            Assert.True(below.Hit, $"{k.Name} at {k.Position} floats");
        }
    }

    [Fact]
    public void Player_can_actually_walk_onto_the_mesa_and_ledge_guns()
    {
        var g = Arena.Build(bots: 0);
        g.Player.Move.Position = new Vector3(850, 28, 0);
        for (int i = 0; i < 170; i++) g.Tick(new UserCmd { Forward = 1, Yaw = 90 }, false);    // up the +X mesa stairs
        for (int i = 0; i < 40; i++) g.Tick(new UserCmd { Forward = 1, Yaw = 90 }, false);
        Assert.Contains(WeaponId.SuperShotgun, g.Player.Owned);

        g.Player.Move.Position = new Vector3(1300, 28, 0);
        for (int i = 0; i < 400; i++) g.Tick(new UserCmd { Forward = 1, Yaw = -90 }, false);  // up the east ledge stairs
        Assert.Contains(WeaponId.RocketLauncher, g.Player.Owned);
    }

    [Fact]
    public void Bots_on_the_real_map_collect_pickups_over_time()
    {
        var g = Arena.Build(bots: 1);
        g.Console.Execute("sv_cheats 1; god", echo: false);
        g.Player.Move.Position = new Vector3(-1900, 28, 1900);   // parked in a corner
        int pickups = 0;
        for (int i = 0; i < 240 * (int)GameWorld.TickRate; i++)
        {
            g.Tick(default, false);
            pickups += g.Events.Count(e => e.Kind == EventKind.Pickup && e.B.X == 0);
            g.Events.Clear();
        }
        Assert.True(pickups >= 3, $"bot only collected {pickups} items in 4 minutes");
    }
}
