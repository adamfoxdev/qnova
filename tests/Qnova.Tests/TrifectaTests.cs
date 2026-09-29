using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class TrifectaTests
{
    /// <summary>Big flat world, player at the origin facing -Z (yaw 0).</summary>
    static GameWorld Flat()
    {
        var w = new World();
        w.Add(new(-20000, -64, -20000), new(20000, 0, 20000));
        var g = new GameWorld(w, new Vector3(0, 28, 0), seed: 11);
        g.SpawnPoints.Add(new Vector3(0, 28, 0));
        g.Tick(default, false);
        return g;
    }

    static Target Dummy(GameWorld g, float z, float x = 0, int hp = 500)
    {
        var t = new Target { Origin = new Vector3(x, 28, z), Health = hp };
        g.Targets.Add(t);
        return t;
    }

    static void Equip(GameWorld g, WeaponId w)
    {
        g.Player.Owned.Add(w); g.Player.Current = w;
        g.Player.Cells = 200; g.Player.Slugs = 20;
    }

    static void Fire(GameWorld g, float seconds)
    {
        for (int i = 0; i < (int)(seconds * GameWorld.TickRate); i++) g.Tick(default, true);
    }

    // ---------------- definitions ----------------

    [Fact]
    public void Weapon_table_has_nine_weapons_in_slot_order()
    {
        Assert.Equal(9, WeaponDef.All.Length);
        Assert.Equal(WeaponId.LightningGun, WeaponDef.All[7].Id);
        Assert.Equal(WeaponId.Railgun, WeaponDef.All[8].Id);
        var lg = WeaponDef.Get(WeaponId.LightningGun);
        Assert.Equal((8, 0.05f, 768f, AmmoType.Cells), (lg.Damage, lg.Refire, lg.Range, lg.Ammo));
        var rg = WeaponDef.Get(WeaponId.Railgun);
        Assert.Equal((100, 1.5f, AmmoType.Slugs), (rg.Damage, rg.Refire, rg.Ammo));
        Assert.Equal("Lightning Gun", lg.Name);
        Assert.Equal("Railgun", rg.Name);
    }

    [Fact]
    public void Number_keys_8_and_9_are_bound_and_console_names_resolve()
    {
        var kb = new KeyBindings();
        Assert.Equal("EIGHT", kb.Get(InputAction.Weapon8));
        Assert.Equal("NINE", kb.Get(InputAction.Weapon9));
        Assert.Equal(InputAction.Weapon1 + 8, InputAction.Weapon9);      // frontend indexes weapons as Weapon1 + n
        Assert.True(GameCommands.TryWeapon("lg", out var a) && a == WeaponId.LightningGun);
        Assert.True(GameCommands.TryWeapon("rail", out var b) && b == WeaponId.Railgun);
        Assert.True(GameCommands.TryWeapon("9", out var c) && c == WeaponId.Railgun);
    }

    // ---------------- lightning gun ----------------

    [Fact]
    public void Lightning_gun_deals_8_per_hit_about_18_times_a_second_and_burns_cells()
    {
        var g = Flat();
        Equip(g, WeaponId.LightningGun);
        var t = Dummy(g, -300);
        int cells = g.Player.Cells;
        Fire(g, 1.0f);
        int dealt = 500 - t.Health;
        Assert.Equal(0, dealt % 8);
        Assert.InRange(dealt / 8, 15, 20);                         // 0.05s refire on a 72 Hz tick
        Assert.Equal(cells - dealt / 8, g.Player.Cells);           // one cell per hit-tick
    }

    [Fact]
    public void Lightning_range_is_768_units()
    {
        var g = Flat();
        Equip(g, WeaponId.LightningGun);
        var near = Dummy(g, -700);
        var far = Dummy(g, -900, x: 400);                           // out of line anyway; separate check below
        Fire(g, 0.3f);
        Assert.True(near.Health < 500, "700 units away is in range (box face ~684)");

        var g2 = Flat();
        Equip(g2, WeaponId.LightningGun);
        var beyond = Dummy(g2, -900);
        Fire(g2, 0.5f);
        Assert.Equal(500, beyond.Health);                           // 900 is out of reach
    }

    [Fact]
    public void Lightning_beam_hits_only_the_first_target_and_emits_styled_tracers()
    {
        var g = Flat();
        Equip(g, WeaponId.LightningGun);
        var first = Dummy(g, -200);
        var behind = Dummy(g, -500);
        Fire(g, 0.3f);
        Assert.True(first.Health < 500);
        Assert.Equal(500, behind.Health);                           // no piercing
        Assert.Contains(g.Events, e => e.Kind == EventKind.Tracer && e.Arg == (int)WeaponId.LightningGun);
        Assert.Contains(g.Events, e => e.Kind == EventKind.Shot && e.Arg == (int)WeaponId.LightningGun);
    }

    [Fact]
    public void Lightning_gun_is_dry_without_cells_and_kill_messages_name_it()
    {
        var g = Flat();
        Equip(g, WeaponId.LightningGun);
        g.Player.Cells = 0;
        Fire(g, 0.5f);
        Assert.Contains(g.Events, e => e.Kind == EventKind.DryFire);
        Assert.DoesNotContain(g.Events, e => e.Kind == EventKind.Tracer);

        var g2 = Flat();
        var bot = g2.AddBot();
        g2.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -250);
        bot.Body.Health = 40;
        Equip(g2, WeaponId.LightningGun);
        Fire(g2, 1.0f);
        Assert.False(bot.Body.Alive);
        Assert.Contains(g2.Console.Lines, l => l.Contains("You killed Bot1 with the Lightning Gun"));
    }

    // ---------------- railgun ----------------

    [Fact]
    public void Rail_is_instant_100_damage_and_one_shots_a_full_health_bot()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -3000);
        Equip(g, WeaponId.Railgun);
        int slugs = g.Player.Slugs;
        g.Tick(default, true);                                       // a single tick
        Assert.False(bot.Body.Alive, "100 damage kills a 100 HP player in one hit, at 3000 units");
        Assert.Equal(slugs - 1, g.Player.Slugs);
        Assert.Contains(g.Console.Lines, l => l.Contains("You killed Bot1 with the Railgun"));
        Assert.Contains(g.Events, e => e.Kind == EventKind.Tracer && e.Arg == (int)WeaponId.Railgun);
    }

    [Fact]
    public void Rail_pierces_everything_in_line_but_not_walls()
    {
        var g = Flat();
        Equip(g, WeaponId.Railgun);
        var a = Dummy(g, -300);
        var b = Dummy(g, -700);
        var c = Dummy(g, -1500);
        g.Map.Add(new(-200, 0, -2100), new(200, 400, -2000));        // wall behind them
        var behindWall = Dummy(g, -2400);
        g.Tick(default, true);
        Assert.Equal(400, a.Health);
        Assert.Equal(400, b.Health);
        Assert.Equal(400, c.Health);
        Assert.Equal(500, behindWall.Health);                        // the wall stops it
        var trail = g.Events.Single(e => e.Kind == EventKind.Tracer && e.Arg == (int)WeaponId.Railgun);
        Assert.InRange(trail.B.Z, -2001f, -1999f);                   // trail ends at the wall
    }

    [Fact]
    public void Rail_can_hit_a_bot_and_a_dummy_with_one_shot()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -600);
        var t = Dummy(g, -1200);
        Equip(g, WeaponId.Railgun);
        g.Tick(default, true);
        Assert.False(bot.Body.Alive);
        Assert.Equal(400, t.Health);
        Assert.Equal(1, g.Player.Frags);                              // the bot; dummies respawn but count too only when killed
    }

    [Fact]
    public void Rail_refire_is_one_and_a_half_seconds()
    {
        var g = Flat();
        Equip(g, WeaponId.Railgun);
        g.Player.Slugs = 10;
        Dummy(g, -500);
        Fire(g, 2.9f);                                               // shots at t=0 and t=1.5
        Assert.Equal(8, g.Player.Slugs);
        Fire(g, 0.5f);                                               // third shot at t=3.0
        Assert.Equal(7, g.Player.Slugs);
    }

    [Fact]
    public void Rail_without_slugs_clicks_dry()
    {
        var g = Flat();
        Equip(g, WeaponId.Railgun);
        g.Player.Slugs = 0;
        g.Tick(default, true);
        Assert.Contains(g.Events, e => e.Kind == EventKind.DryFire);
        Assert.DoesNotContain(g.Events, e => e.Kind == EventKind.Tracer);
    }

    [Fact]
    public void Rail_does_not_hurt_or_knock_back_its_owner()
    {
        var g = Flat();
        Equip(g, WeaponId.Railgun);
        g.Player.Pitch = -90;                                        // straight down into the floor
        g.Tick(new UserCmd { Pitch = -90 }, true);
        Assert.Equal(100, g.Player.Health);
        Assert.True(MathF.Abs(g.Player.Move.Velocity.Y) < 1f);
    }

    // ---------------- bots ----------------

    [Fact]
    public void Bots_pick_lightning_at_mid_range_and_rail_at_long_range_when_skilled()
    {
        WeaponId Chosen(float dist, int skill, float seconds = 2f)
        {
            var g = Flat();
            g.BotSkill = skill;
            var bot = g.AddBot();
            bot.Body.Move.Position = new Vector3(0, 28, -dist);
            g.Player.Health = 1000; g.Player.God = true;
            for (int i = 0; i < (int)(seconds * GameWorld.TickRate); i++) g.Tick(default, false);
            return bot.Body.Current;
        }
        Assert.Equal(WeaponId.LightningGun, Chosen(500, 4));
        Assert.Equal(WeaponId.Railgun, Chosen(2200, 5));
        Assert.Equal(WeaponId.SuperNailgun, Chosen(2200, 1));        // novices don't get the rail
        Assert.Equal(WeaponId.SuperShotgun, Chosen(120, 4, seconds: 0.1f));    // right after first sight, before it backs off
    }

    // ---------------- ammo, pickups and cheats ----------------

    [Fact]
    public void Weapon_pickups_bring_cells_and_slugs_and_boxes_respect_the_caps()
    {
        var g = Flat();
        g.Player.Cells = 0; g.Player.Slugs = 0;
        g.Pickups.Add(new Pickup { Kind = PickupKind.Weapon, Weapon = WeaponId.LightningGun, Position = new Vector3(0, 16, 0) });
        g.Tick(default, false);
        Assert.Contains(WeaponId.LightningGun, g.Player.Owned);
        Assert.Equal(50, g.Player.Cells);
        Assert.Equal(WeaponId.LightningGun, g.Player.Current);       // auto-switches up

        g.Pickups.Add(new Pickup { Kind = PickupKind.Weapon, Weapon = WeaponId.Railgun, Position = new Vector3(0, 16, 0) });
        g.Tick(default, false);
        Assert.Equal(10, g.Player.Slugs);
        Assert.Equal(WeaponId.Railgun, g.Player.Current);

        g.Player.Cells = 190; g.Player.Slugs = 48;
        g.Pickups.Add(new Pickup { Kind = PickupKind.Cells, Amount = 60, Position = new Vector3(0, 16, 0) });
        g.Pickups.Add(new Pickup { Kind = PickupKind.Slugs, Amount = 10, Position = new Vector3(0, 16, 0) });
        g.Tick(default, false);
        Assert.Equal(Player.MaxCells, g.Player.Cells);
        Assert.Equal(Player.MaxSlugs, g.Player.Slugs);
    }

    [Fact]
    public void Cheats_give_cells_slugs_and_all_nine_weapons()
    {
        var g = Flat();
        g.Console.Execute("sv_cheats 1; giveall");
        Assert.Equal(9, g.Player.Owned.Count);
        Assert.Equal(Player.MaxCells, g.Player.Cells);
        Assert.Equal(Player.MaxSlugs, g.Player.Slugs);

        var g2 = Flat();
        g2.Console.Execute("sv_cheats 1; give cells 30; give slugs 4; give lg; weapon rail");
        Assert.Equal(80, g2.Player.Cells);
        Assert.Equal(9, g2.Player.Slugs);
        Assert.Contains(WeaponId.LightningGun, g2.Player.Owned);
        g2.Console.Execute("give rail; weapon 9");
        Assert.Equal(WeaponId.Railgun, g2.Player.Current);
        g2.Console.Execute("stats");
        Assert.Contains(g2.Console.Lines, l => l.Contains("cells") && l.Contains("slugs"));
    }

    [Fact]
    public void Respawn_resets_cells_and_slugs_and_drops_the_new_guns_for_humans()
    {
        var g = Flat();
        Equip(g, WeaponId.Railgun); Equip(g, WeaponId.LightningGun);
        g.Player.Cells = 3; g.Player.Slugs = 0;
        g.Console.Execute("kill");
        for (int i = 0; i < 4 * (int)GameWorld.TickRate; i++) g.Tick(default, false);
        Assert.True(g.Player.Alive);
        Assert.DoesNotContain(WeaponId.Railgun, g.Player.Owned);
        Assert.DoesNotContain(WeaponId.LightningGun, g.Player.Owned);
        Assert.Equal(50, g.Player.Cells);
        Assert.Equal(5, g.Player.Slugs);
    }

    [Fact]
    public void Arena_places_the_new_guns_on_valid_reachable_spots()
    {
        var g = Arena.Build(bots: 0);
        var lg = g.Pickups.Single(k => k.Kind == PickupKind.Weapon && k.Weapon == WeaponId.LightningGun);
        var rg = g.Pickups.Single(k => k.Kind == PickupKind.Weapon && k.Weapon == WeaponId.Railgun);
        Assert.True(g.Map.IsEmpty(lg.Position, Pickup.Half) && g.Map.IsEmpty(rg.Position, Pickup.Half));

        // the railgun sits on the east ledge (y 128 + 16), reachable by the stairs
        Assert.InRange(rg.Position.Y, 140f, 150f);
        g.Player.Move.Position = new Vector3(1300, 28, 0);
        for (int i = 0; i < 400; i++) g.Tick(new UserCmd { Forward = 1, Yaw = -90 }, false);   // up the stairs onto the ledge
        Assert.InRange(g.Player.Move.Position.Y, 150f, 160f);
    }
}
