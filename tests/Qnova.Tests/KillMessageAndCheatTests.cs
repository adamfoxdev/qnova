using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class KillMessageAndCheatTests
{
    static GameWorld Flat(int bots = 0)
    {
        var w = new World();
        w.Add(new(-3000, -64, -3000), new(3000, 0, 3000));
        var g = new GameWorld(w, new Vector3(0, 28, 0), seed: 5);
        g.SpawnPoints.Add(new Vector3(0, 28, 0));
        for (int i = 0; i < bots; i++) g.AddBot();
        g.Tick(default, false);
        return g;
    }

    static bool Said(GameWorld g, string text) => g.Console.Lines.Any(l => l.Contains(text));

    // ---- kill messages name the weapon ----

    [Fact]
    public void Human_kill_with_super_shotgun_names_the_gun()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -150);
        g.Player.Owned.Add(WeaponId.SuperShotgun);
        g.Player.Current = WeaponId.SuperShotgun; g.Player.Shells = 50;
        for (int i = 0; i < 3 * (int)GameWorld.TickRate; i++) g.Tick(new UserCmd { Pitch = -2f }, true);
        Assert.False(bot.Body.Alive);
        Assert.True(Said(g, "You killed Bot1 with the Double Shotgun"), string.Join(" | ", g.Console.Lines));
    }

    [Fact]
    public void Bot_rocket_kill_names_the_rocket_launcher()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -400);
        g.Player.Health = 1;
        // splash from the bot's rocket, credited to it
        g.RadiusDamage(new Vector3(0, 10, -20), 160, 120, null, bot.Body, WeaponId.RocketLauncher);
        Assert.False(g.Player.Alive);
        Assert.True(Said(g, "Bot1 killed You with the Rocket Launcher"));
    }

    [Fact]
    public void Kill_by_projectile_carries_the_weapon_through_the_flight()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -500);
        bot.Body.Health = 5;
        g.Player.Owned.Add(WeaponId.RocketLauncher);
        g.Player.Current = WeaponId.RocketLauncher;
        for (int i = 0; i < 2 * (int)GameWorld.TickRate; i++) g.Tick(default, i == 0);
        Assert.False(bot.Body.Alive);
        Assert.True(Said(g, "You killed Bot1 with the Rocket Launcher"));

        // nails too
        var g2 = Flat();
        var b2 = g2.AddBot();
        g2.Console.Execute("bot_ai 0", echo: false);
        b2.Body.Move.Position = new Vector3(0, 28, -500);
        b2.Body.Health = 3;
        g2.Player.Owned.Add(WeaponId.Nailgun);
        g2.Player.Current = WeaponId.Nailgun;
        for (int i = 0; i < 2 * (int)GameWorld.TickRate; i++) g2.Tick(default, i == 0);
        Assert.True(Said(g2, "You killed Bot1 with the Nailgun"));
    }

    [Fact]
    public void Axe_and_grenade_kills_are_named_too()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -60);
        bot.Body.Health = 10;
        g.Player.Current = WeaponId.Axe;
        g.Tick(default, true);
        Assert.True(Said(g, "You killed Bot1 with the Axe"));

        g.RadiusDamage(new Vector3(0, 10, 0), 160, 120, null, bot.Body, WeaponId.GrenadeLauncher);   // splash on the player
        g.Player.Health = 1;
        g.RadiusDamage(new Vector3(0, 10, -20), 160, 120, null, bot.Body, WeaponId.GrenadeLauncher);
        Assert.True(Said(g, "Bot1 killed You with the Grenade Launcher"));
    }

    [Fact]
    public void Self_kill_with_your_own_rocket_reads_naturally()
    {
        var g = Flat();
        g.Player.Health = 1;
        g.RadiusDamage(new Vector3(0, 10, 0), 160, 120, null, g.Player, WeaponId.RocketLauncher);
        Assert.False(g.Player.Alive);
        Assert.True(Said(g, "You suicided (Rocket Launcher)"));
        Assert.Equal(-1, g.Player.Frags);
    }

    [Fact]
    public void Console_kill_and_unknown_weapon_have_no_weapon_suffix()
    {
        var g = Flat();
        g.Console.Execute("kill");
        Assert.Contains("You suicided", g.Console.Lines);          // exact line, no "(...)"
        var g2 = Flat();
        var bot = g2.AddBot();
        g2.Player.Health = 1;
        g2.DamagePlayer(g2.Player, 50, default, bot.Body, knock: false);   // no weapon supplied
        Assert.Contains("Bot1 killed You", g2.Console.Lines);
    }

    // ---- cheat commands ----

    [Fact]
    public void Giveall_gives_every_weapon_and_full_ammo_without_healing()
    {
        var g = Flat();
        g.Player.Health = 40;
        g.Console.Execute("giveall");
        Assert.True(Said(g, "is a cheat"));                        // gated by sv_cheats
        Assert.DoesNotContain(WeaponId.RocketLauncher, g.Player.Owned);

        g.Console.Execute("sv_cheats 1; giveall");
        Assert.Equal(Enum.GetValues<WeaponId>().Length, g.Player.Owned.Count);
        Assert.Equal(Player.MaxShells, g.Player.Shells);
        Assert.Equal(Player.MaxNails, g.Player.Nails);
        Assert.Equal(Player.MaxRockets, g.Player.Rockets);
        Assert.Equal(40, g.Player.Health);                          // untouched, unlike "give all"

        g.Console.Execute("give all");
        Assert.Equal(100, g.Player.Health);
    }

    [Fact]
    public void Impulse_9_is_the_quake_alias()
    {
        var g = Flat();
        g.Console.Execute("sv_cheats 1; impulse 9");
        Assert.Contains(WeaponId.SuperNailgun, g.Player.Owned);
        Assert.Equal(Player.MaxRockets, g.Player.Rockets);
        g.Console.Execute("impulse 5");
        Assert.True(Said(g, "only impulse 9"));
    }

    [Fact]
    public void Turning_cheats_off_does_not_take_the_weapons_back()
    {
        var g = Flat();
        g.Console.Execute("sv_cheats 1; giveall; sv_cheats 0");
        Assert.Contains(WeaponId.RocketLauncher, g.Player.Owned);   // items are yours; only cheat modes revert
        g.Console.Execute("giveall");
        Assert.True(Said(g, "is a cheat"));
    }

    // ---- zoom binding ----

    [Fact]
    public void Zoom_is_bound_to_mouse3_by_default_and_rebindable_from_the_console()
    {
        var g = Flat();
        Assert.Equal("MOUSE3", g.Bindings.Get(InputAction.Zoom));
        g.Console.Execute("bind Z zoom");
        Assert.Equal("Z", g.Bindings.Get(InputAction.Zoom));
        g.Console.Execute("bind MOUSE3 jump");
        Assert.Equal("MOUSE3", g.Bindings.Get(InputAction.Jump));
        g.Console.Execute("bindlist");
        Assert.Contains(g.Console.Lines, l => l.StartsWith("zoom") && l.Contains("Z"));
    }
}
