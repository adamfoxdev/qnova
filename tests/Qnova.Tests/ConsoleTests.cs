using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class ConsoleTests
{
    static GameWorld Flat()
    {
        var w = new World();
        w.Add(new(-10000, -64, -10000), new(10000, 0, 10000));
        w.Add(new(-100, 0, -300), new(100, 200, -280));   // wall ahead (-Z)
        var g = new GameWorld(w, new Vector3(0, 28, 0));
        g.Tick(default, false);
        return g;
    }

    static bool Said(GameWorld g, string text) => g.Console.Lines.Any(l => l.Contains(text, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Tokenizer_handles_quotes_semicolons_and_comments()
    {
        Assert.Equal(new[] { "echo", "a b", "c" }, GameConsole.Tokenize("echo \"a b\" c"));
        Assert.Equal(new[] { "a x", "b", "\"c;d\"" }, GameConsole.SplitStatements("a x; b ;\"c;d\" // comment; ignored"));
    }

    [Fact]
    public void Printing_and_setting_a_cvar()
    {
        var g = Flat();
        g.Console.Execute("sv_gravity");
        Assert.True(Said(g, "sv_gravity = 800"));
        g.Console.Execute("sv_gravity 400");
        Assert.Equal(400f, g.Settings.Gravity);
        Assert.Equal(400f, g.Console.Get("sv_gravity"));
        g.Console.Execute("set sv_gravity 600; sv_maxspeed 500");
        Assert.Equal(600f, g.Settings.Gravity);
        Assert.Equal(500f, g.Settings.MaxSpeed);
    }

    [Fact]
    public void Bad_input_is_reported_not_thrown()
    {
        var g = Flat();
        g.Console.Execute("nonsense");
        Assert.True(Said(g, "Unknown command"));
        g.Console.Execute("sv_gravity abc");
        Assert.True(Said(g, "not a number"));
        Assert.Equal(800f, g.Settings.Gravity);
    }

    [Fact]
    public void Lower_gravity_and_higher_jump_speed_jump_higher()
    {
        float Apex(string cfg)
        {
            var g = Flat();
            g.Console.Execute(cfg, echo: false);
            float max = 0;
            g.Tick(new UserCmd { Jump = true }, false);
            for (int i = 0; i < 300; i++) { g.Tick(default, false); max = Math.Max(max, g.Player.Move.Position.Y - 28); }
            return max;
        }
        float normal = Apex("");
        Assert.True(Apex("sv_gravity 200") > normal * 3);
        Assert.True(Apex("sv_jumpspeed 540") > normal * 3);
    }

    [Fact]
    public void Autohop_cvar_rejumps_when_holding_space()
    {
        int Takeoffs(string cfg)
        {
            var g = Flat();
            g.Console.Execute(cfg, echo: false);
            var jump = new UserCmd { Jump = true };
            int n = 0; float prevVy = 0;
            for (int i = 0; i < 400; i++)
            {
                g.Tick(jump, false);
                float vy = g.Player.Move.Velocity.Y;
                if (prevVy < 200 && vy > 250) n++;
                prevVy = vy;
            }
            return n;
        }
        Assert.Equal(1, Takeoffs(""));            // holding space = one jump
        Assert.True(Takeoffs("sv_autohop 1") >= 5);
    }

    [Fact]
    public void Cheats_are_gated_by_sv_cheats()
    {
        var g = Flat();
        g.Console.Execute("give all");
        Assert.True(Said(g, "is a cheat"));
        g.Console.Execute("sv_infiniteammo 1");
        Assert.False(g.InfiniteAmmo);
        g.Console.Execute("sv_cheats 1; sv_infiniteammo 1");
        Assert.True(g.InfiniteAmmo);
    }

    [Fact]
    public void Disabling_cheats_reverts_cheat_cvars()
    {
        var g = Flat();
        g.Console.Execute("sv_cheats 1; god; noclip; sv_infiniteammo 1");
        Assert.True(g.Player.God && g.Player.Move.NoClip && g.InfiniteAmmo);
        g.Console.Execute("sv_cheats 0");
        Assert.False(g.Player.God || g.Player.Move.NoClip || g.InfiniteAmmo);
    }

    [Fact]
    public void God_and_infinite_ammo_work()
    {
        var g = Flat();
        g.Console.Execute("sv_cheats 1; god; sv_infiniteammo 1; give rl");
        Assert.Equal(WeaponId.RocketLauncher, g.Player.Current);
        var down = new UserCmd { Pitch = -90 };
        g.Tick(down, true);
        for (int i = 0; i < 30; i++) g.Tick(down, false);
        Assert.Equal(100, g.Player.Health);
        Assert.Equal(10, g.Player.Rockets);
        g.Console.Execute("god");
        Assert.False(g.Player.God);
    }

    [Fact]
    public void Noclip_passes_through_walls()
    {
        var g = Flat();
        g.Console.Execute("sv_cheats 1; noclip");
        var fly = new UserCmd { Forward = 1 };
        for (int i = 0; i < 200; i++) g.Tick(fly, false);
        Assert.True(g.Player.Move.Position.Z < -320, "should pass the wall at z=-300");
    }

    [Fact]
    public void Give_setpos_getpos_and_spawn()
    {
        var g = Flat();
        g.Console.Execute("sv_cheats 1; give rockets 50; give health 40; setpos 10 100 20; getpos");
        Assert.Equal(60, g.Player.Rockets);
        Assert.Equal(40, g.Player.Health);
        Assert.Equal(new Vector3(10, 100, 20), g.Player.Move.Position);
        Assert.True(Said(g, "pos 10 100 20"));

        g.Console.Execute("setpos 0 28 0; spawn 3");
        Assert.Equal(3, g.Targets.Count);
        g.Console.Execute("killtargets");
        Assert.Empty(g.Targets);
    }

    [Fact]
    public void Weapon_names_and_numbers_resolve()
    {
        var g = Flat();
        g.Console.Execute("sv_cheats 1; give all");
        g.Console.Execute("weapon ssg");
        Assert.Equal(WeaponId.SuperShotgun, g.Player.Current);
        g.Console.Execute("weapon 7");
        Assert.Equal(WeaponId.RocketLauncher, g.Player.Current);
        g.Console.Execute("weapon banana");
        Assert.Equal(WeaponId.RocketLauncher, g.Player.Current);
    }

    [Fact]
    public void Toggle_reset_and_respawn()
    {
        var g = Flat();
        g.Console.Execute("toggle sv_autohop");
        Assert.True(g.Player.Move.AutoHop);
        g.Console.Execute("sv_friction 0; reset sv_friction");
        Assert.Equal(4f, g.Settings.Friction);
        g.Console.Execute("sv_gravity 1; reset all");
        Assert.Equal(800f, g.Settings.Gravity);
        Assert.False(g.Player.Move.AutoHop);

        g.Player.Health = 0; g.Player.Move.Position = new Vector3(500, 50, 500);
        g.Console.Execute("respawn");
        Assert.Equal(100, g.Player.Health);
        Assert.Equal(g.SpawnPoint, g.Player.Move.Position);
    }

    [Fact]
    public void Completion_and_help()
    {
        var g = Flat();
        var c = g.Console.Complete("sv_air");
        Assert.Contains("sv_airaccelerate", c);
        Assert.Contains("sv_aircap", c);
        Assert.DoesNotContain("sv_gravity", c);
        g.Console.Execute("help sv_gravity");
        Assert.True(Said(g, "Gravity in units"));
        g.Console.Execute("find noclip");
        Assert.True(Said(g, "sv_noclip"));
    }
}
