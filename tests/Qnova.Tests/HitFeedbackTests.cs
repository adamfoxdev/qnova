using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class HitFeedbackTests
{
    static GameWorld Flat()
    {
        var w = new World();
        w.Add(new(-20000, -64, -20000), new(20000, 0, 20000));
        var g = new GameWorld(w, new Vector3(0, 28, 0), seed: 21);
        g.SpawnPoints.Add(new Vector3(0, 28, 0));
        g.Tick(default, false);
        return g;
    }

    static Target Dummy(GameWorld g, float z, int hp = 500)
    {
        var t = new Target { Origin = new Vector3(0, 28, z), Health = hp };
        g.Targets.Add(t);
        return t;
    }

    static HurtFlags Flags(GameEvent e) => (HurtFlags)(int)e.B.Y;

    // ---------------- hurt events carry damage, victim and attacker ----------------

    [Fact]
    public void Hitting_a_dummy_reports_damage_victim_id_and_that_you_did_it()
    {
        var g = Flat();
        Dummy(g, -900);                                            // index 0, out of the way
        var t = Dummy(g, -300);                                    // index 1
        g.Player.Current = WeaponId.Shotgun;
        g.Events.Clear();
        g.Tick(default, true);
        var hurts = g.Events.Where(e => e.Kind == EventKind.Hurt).ToList();
        Assert.NotEmpty(hurts);
        Assert.All(hurts, e =>
        {
            Assert.Equal(1001, e.Arg);                             // 1000 + index of the target hit
            Assert.Equal(4f, e.B.X);                               // shotgun pellet damage
            Assert.True(Flags(e).HasFlag(HurtFlags.ByHuman));
            Assert.False(Flags(e).HasFlag(HurtFlags.VictimHuman));
            Assert.False(Flags(e).HasFlag(HurtFlags.Killing));
        });
        Assert.Equal(500 - 4 * hurts.Count, t.Health);
    }

    [Fact]
    public void Killing_blow_is_flagged()
    {
        var g = Flat();
        var t = Dummy(g, -300, hp: 100);
        g.Player.Current = WeaponId.Railgun; g.Player.Slugs = 5;
        g.Events.Clear();
        g.Tick(default, true);
        var e = g.Events.Single(x => x.Kind == EventKind.Hurt);
        Assert.Equal(100f, e.B.X);
        Assert.True(Flags(e).HasFlag(HurtFlags.Killing | HurtFlags.ByHuman));
        Assert.False(t.Alive);
    }

    [Fact]
    public void Players_have_distinct_ids_and_bot_hits_are_attributed()
    {
        var g = Flat();
        var b1 = g.AddBot(); var b2 = g.AddBot();
        Assert.Equal(0, g.Player.Id);
        Assert.Equal(1, b1.Body.Id);
        Assert.Equal(2, b2.Body.Id);

        g.Console.Execute("bot_ai 0", echo: false);
        b2.Body.Move.Position = new Vector3(0, 28, -300);
        b1.Body.Move.Position = new Vector3(2000, 28, 2000);
        g.Player.Current = WeaponId.SuperShotgun; g.Player.Shells = 50;
        g.Events.Clear();
        g.Tick(default, true);
        var hurt = g.Events.Where(e => e.Kind == EventKind.Hurt).ToList();
        Assert.NotEmpty(hurt);
        Assert.All(hurt, e => { Assert.Equal(2, e.Arg); Assert.True(Flags(e).HasFlag(HurtFlags.ByHuman)); });
    }

    [Fact]
    public void Damage_to_the_human_is_flagged_as_victim_human_and_not_by_human()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Events.Clear();
        g.DamagePlayer(g.Player, 30, default, bot.Body, knock: false, WeaponId.RocketLauncher);
        var e = g.Events.Single(x => x.Kind == EventKind.Hurt);
        Assert.Equal(30f, e.B.X);
        Assert.Equal(0, e.Arg);
        Assert.True(Flags(e).HasFlag(HurtFlags.VictimHuman));
        Assert.False(Flags(e).HasFlag(HurtFlags.ByHuman));

        // your own rocket splash is "by human" and "victim human" at once
        g.Events.Clear();
        g.RadiusDamage(new Vector3(0, 10, 0), 160, 120, null, g.Player, WeaponId.RocketLauncher);
        var self = g.Events.First(x => x.Kind == EventKind.Hurt);
        Assert.True(Flags(self).HasFlag(HurtFlags.VictimHuman | HurtFlags.ByHuman));
    }

    [Fact]
    public void Lightning_produces_a_stream_of_small_hurt_events_to_aggregate()
    {
        var g = Flat();
        Dummy(g, -300);
        g.Player.Current = WeaponId.LightningGun; g.Player.Cells = 100;
        g.Events.Clear();
        for (int i = 0; i < 72; i++) g.Tick(default, true);
        var hurts = g.Events.Where(e => e.Kind == EventKind.Hurt).ToList();
        Assert.InRange(hurts.Count, 15, 20);
        Assert.All(hurts, e => Assert.Equal(8f, e.B.X));
    }

    [Fact]
    public void Splash_damage_reports_the_falloff_amount()
    {
        var g = Flat();
        var t = Dummy(g, -300, hp: 500);
        g.Events.Clear();
        g.RadiusDamage(new Vector3(0, 28, -300 + 40), 160, 120, null, g.Player, WeaponId.RocketLauncher);   // 40 units away: 120 - 20
        var e = g.Events.Single(x => x.Kind == EventKind.Hurt);
        Assert.Equal(100f, e.B.X);
        Assert.Equal(400, t.Health);
    }

    // ---------------- crosshair: can we hit an enemy right now? ----------------

    [Fact]
    public void Crosshair_is_on_target_only_when_an_enemy_is_the_first_thing_in_the_way()
    {
        var g = Flat();
        g.Player.Current = WeaponId.Shotgun;
        Assert.False(g.AimingAtEnemy(g.Player));                   // empty room

        var t = Dummy(g, -500);
        Assert.True(g.AimingAtEnemy(g.Player));

        g.Map.Add(new(-100, 0, -300), new(100, 300, -280));        // wall between us
        Assert.False(g.AimingAtEnemy(g.Player));

        g.Player.Pitch = 60;                                       // aimed off target
        Assert.False(g.AimingAtEnemy(g.Player));
    }

    [Fact]
    public void Crosshair_respects_the_weapons_reach()
    {
        var g = Flat();
        Dummy(g, -900);
        g.Player.Current = WeaponId.LightningGun;
        Assert.False(g.AimingAtEnemy(g.Player), "900 units is beyond the lightning gun's 768");
        g.Player.Current = WeaponId.Railgun;
        Assert.True(g.AimingAtEnemy(g.Player));
        g.Player.Current = WeaponId.Axe;
        Assert.False(g.AimingAtEnemy(g.Player));

        var g2 = Flat();
        Dummy(g2, -60);                                            // face ~44 units away
        g2.Player.Current = WeaponId.Axe;
        Assert.True(g2.AimingAtEnemy(g2.Player));
    }

    [Fact]
    public void Crosshair_sees_bots_but_not_dead_ones_or_yourself()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -400);
        g.Player.Current = WeaponId.RocketLauncher;
        Assert.True(g.AimingAtEnemy(g.Player));
        bot.Body.Health = 0;
        Assert.False(g.AimingAtEnemy(g.Player));

        var g2 = Flat();
        g2.Player.Current = WeaponId.Railgun;
        g2.Player.Pitch = -90;                                     // straight at the floor under our own feet
        Assert.False(g2.AimingAtEnemy(g2.Player));

        var d = Dummy(g2, -300); g2.Player.Pitch = 0;
        d.Health = 0;
        Assert.False(g2.AimingAtEnemy(g2.Player));                 // a dead dummy isn't a target
        g2.Console.Execute("kill");
        d.Health = 100;
        Assert.False(g2.AimingAtEnemy(g2.Player));                 // and a dead player can't aim
    }
}
