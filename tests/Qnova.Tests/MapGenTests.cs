using System.Diagnostics;
using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class MapGenTests
{
    static readonly int[] Seeds = Enumerable.Range(1, 30).Concat(new[] { 42, 777, 12345, 999983 }).ToArray();

    // ---------------- determinism and validity ----------------

    [Fact]
    public void Same_seed_gives_the_identical_map_and_different_seeds_differ()
    {
        var a = MapGenerator.Generate(1234);
        var b = MapGenerator.Generate(1234);
        Assert.Equal(a.Solids, b.Solids);
        Assert.Equal(a.Pickups.Select(k => (k.Kind, k.Weapon, k.Position)), b.Pickups.Select(k => (k.Kind, k.Weapon, k.Position)));
        Assert.Equal(a.JumpPads.Select(p => (p.Trigger, p.Target)), b.JumpPads.Select(p => (p.Trigger, p.Target)));
        Assert.Equal(a.Spawns, b.Spawns);

        var c = MapGenerator.Generate(1235);
        Assert.NotEqual(a.Solids, c.Solids);
    }

    [Fact]
    public void Every_generated_map_passes_validation_and_has_the_essentials()
    {
        foreach (int seed in Seeds)
        {
            var m = MapGenerator.Generate(seed);
            Assert.Equal($"Random #{seed}", m.Name);
            Assert.Equal(seed, m.Seed);
            var problems = MapGenerator.Validate(m);
            Assert.True(problems.Count == 0, $"seed {seed}: {string.Join("; ", problems)}");

            Assert.InRange(m.Half, 1536f, 2560f);
            Assert.InRange(m.Height, 640f, 896f);
            Assert.True(m.Spawns.Count >= 8);
            Assert.Equal(m.Spawns[0], m.PlayerSpawn);
            Assert.True(m.Platforms.Count >= 1, $"seed {seed}: no platforms");
            Assert.True(m.Solids.Count > 25);
            Assert.True(m.Lights.Count > 20 && m.Decor.Count > 20);
            Assert.Equal(3, m.Dummies.Count);

            // all seven pickup-able guns, health, and ammo of every kind
            var guns = m.Pickups.Where(k => k.Kind == PickupKind.Weapon).Select(k => k.Weapon).ToHashSet();
            Assert.Equal(7, guns.Count);
            Assert.Contains(WeaponId.Railgun, guns);
            Assert.Contains(WeaponId.LightningGun, guns);
            Assert.True(m.Pickups.Count(k => k.Kind == PickupKind.Health) >= 5);
        }
    }

    [Fact]
    public void Maps_vary_in_size_and_features_across_seeds()
    {
        var maps = Seeds.Select(MapGenerator.Generate).ToList();
        Assert.True(maps.Select(m => m.Half).Distinct().Count() >= 3, "sizes should vary");
        Assert.True(maps.Select(m => m.Height).Distinct().Count() >= 2, "ceiling heights should vary");
        Assert.True(maps.Select(m => m.Platforms.Count).Distinct().Count() >= 2);
        Assert.True(maps.Any(m => m.JumpPads.Count > 0), "some maps should have launch pads");
        Assert.True(maps.Select(m => m.Solids.Count).Distinct().Count() > 10);
    }

    [Fact]
    public void Generation_is_fast_enough_to_run_from_a_menu_click()
    {
        var sw = Stopwatch.StartNew();
        for (int seed = 100; seed < 120; seed++) MapGenerator.Generate(seed);
        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds / 20 < 0.5, $"average {sw.Elapsed.TotalSeconds / 20:0.000}s per map");
    }

    [Fact]
    public void Validation_actually_catches_problems()
    {
        var m = MapGenerator.Generate(5);
        Assert.Empty(MapGenerator.Validate(m));

        var buried = MapGenerator.Generate(5);
        buried.Pickups[0].Position = buried.Spawns[0] + new Vector3(0, 400, 0);           // floating in mid-air
        Assert.Contains(MapGenerator.Validate(buried), p => p.Contains("floats"));

        var walled = MapGenerator.Generate(5);
        // seal the first spawn into a box so the flood fill cannot leave it
        var s = walled.Spawns[0];
        walled.Solids.Add(new Aabb(new(s.X - 90, 0, s.Z - 90), new(s.X - 60, 400, s.Z + 90)));
        walled.Solids.Add(new Aabb(new(s.X + 60, 0, s.Z - 90), new(s.X + 90, 400, s.Z + 90)));
        walled.Solids.Add(new Aabb(new(s.X - 90, 0, s.Z - 90), new(s.X + 90, 400, s.Z - 60)));
        walled.Solids.Add(new Aabb(new(s.X - 90, 0, s.Z + 60), new(s.X + 90, 400, s.Z + 90)));
        Assert.Contains(MapGenerator.Validate(walled), p => p.Contains("not reachable") || p.Contains("blocked"));

        var noGun = MapGenerator.Generate(5);
        noGun.Pickups.RemoveAll(k => k.Kind == PickupKind.Weapon && k.Weapon == WeaponId.Railgun);
        Assert.Contains(MapGenerator.Validate(noGun), p => p.Contains("Railgun"));

        var badPad = MapGenerator.Generate(5);
        var plat = badPad.Platforms[0];
        badPad.JumpPads.Add(new JumpPad { Trigger = new Aabb(new(-48, 0, -48), new(48, 40, 48)), Target = new Vector3(plat.Top.Center.X, 3000, plat.Top.Center.Z) });   // way above the ceiling
        Assert.Contains(MapGenerator.Validate(badPad), p => p.Contains("pad"));
    }

    // ---------------- the map is actually playable ----------------

    [Fact]
    public void Every_platform_can_be_climbed_from_its_stair_foot_by_real_movement()
    {
        foreach (int seed in Seeds.Take(20))
        {
            var g = Arena.Build(bots: 0);
            g.LoadMap(MapGenerator.Generate(seed));
            foreach (var plat in g.Platforms())
            {
                g.Player.Move.Position = plat.Foot; g.Player.Move.Velocity = default;
                float yaw = MathF.Atan2(-plat.Dir.X, -plat.Dir.Z) * 180f / MathF.PI;   // face up the stairs
                float expect = plat.Top.Min.Y + 28f;
                bool reached = false;
                for (int i = 0; i < 6 * (int)GameWorld.TickRate && !reached; i++)
                {
                    g.Tick(new UserCmd { Forward = 1, Yaw = yaw }, false);
                    reached = g.Player.Move.OnGround && MathF.Abs(g.Player.Move.Position.Y - expect) < 3f;
                }
                Assert.True(reached, $"seed {seed}: never reached the platform top (y={expect}); at {g.Player.Move.Position}");
            }
        }
    }

    [Fact]
    public void Every_jump_pad_flies_to_its_platform_in_the_real_simulation()
    {
        int checkedPads = 0;
        foreach (int seed in Seeds)
        {
            var probe = MapGenerator.Generate(seed);
            for (int idx = 0; idx < probe.JumpPads.Count; idx++)
            {
                var g = Arena.Build(bots: 0);
                g.LoadMap(MapGenerator.Generate(seed));
                var pad = g.JumpPads[idx];
                g.Player.Move.Position = new Vector3(pad.Center.X, 28, pad.Center.Z);
                g.Tick(default, false);
                bool landed = false;
                for (int i = 0; i < 8 * (int)GameWorld.TickRate && !landed; i++)
                {
                    g.Tick(default, false);
                    landed = g.Player.Move.OnGround && i > 20;
                }
                Assert.True(landed, $"seed {seed} pad {idx}: never landed");
                var p = g.Player.Move.Position;
                Assert.True(g.Platforms().Any(pl => p.X >= pl.Top.Min.X - 20 && p.X <= pl.Top.Max.X + 20 && p.Z >= pl.Top.Min.Z - 20 && p.Z <= pl.Top.Max.Z + 20
                                                    && MathF.Abs(p.Y - (pl.Top.Min.Y + 28f)) < 4f),
                            $"seed {seed} pad {idx}: landed at {p}, which is not on any platform top");
                checkedPads++;
            }
        }
        Assert.True(checkedPads >= 10, $"only {checkedPads} pads exercised");
    }

    // ---------------- loading maps into a running game ----------------

    [Fact]
    public void Loading_a_map_swaps_the_world_in_place_and_respawns_everyone()
    {
        var g = Arena.Build(bots: 2);
        var worldRef = g.Map;
        var playerRef = g.Player;
        g.Console.Execute("sv_cheats 1; giveall; bind X jump", echo: false);
        g.Player.Frags = 5; g.Player.Deaths = 2; g.Bots[0].Body.Frags = 3;
        int loads = 0; g.MapLoaded += () => loads++;

        var m = MapGenerator.Generate(77);
        g.LoadMap(m);

        Assert.Same(worldRef, g.Map);                          // same World object: players' references stay valid
        Assert.Same(playerRef, g.Player);
        Assert.Equal(1, loads);
        Assert.Equal(m.Solids.Count, g.Map.Solids.Count);
        Assert.Equal(m.Pickups.Count, g.Pickups.Count);
        Assert.Equal(m.JumpPads.Count, g.JumpPads.Count);
        Assert.Equal(m.Decor.Count, g.Decor.Count);
        Assert.Equal(m.Lights.Count, g.Lights.Count);
        Assert.Equal(m.Dummies.Count, g.Targets.Count);
        Assert.Equal(m.Spawns, g.SpawnPoints);
        Assert.Equal(m.PlayerSpawn, g.SpawnPoint);
        Assert.Equal("Random #77", g.MapName);
        Assert.Equal(77, g.MapSeed);
        Assert.Equal(m.Half, g.MapHalf);
        Assert.Equal(m.Height, g.CeilingY);

        Assert.Equal(m.PlayerSpawn, g.Player.Move.Position);
        Assert.Equal(0, g.Player.Frags); Assert.Equal(0, g.Player.Deaths); Assert.Equal(0, g.Bots[0].Body.Frags);
        Assert.Equal(2, g.Bots.Count);
        foreach (var b in g.Bots) { Assert.True(b.Body.Alive); Assert.Contains(b.Body.Move.Position, m.Spawns); }
        Assert.Empty(g.Projectiles);
        Assert.Equal("X", g.Bindings.Get(InputAction.Jump));   // bindings, console and settings survive
        Assert.True(g.Console.CheatsOn);
    }

    [Fact]
    public void Old_map_geometry_is_really_gone_and_the_classic_arena_can_be_restored()
    {
        var g = Arena.Build(bots: 0);
        int classicSolids = g.Map.Solids.Count;
        g.LoadMap(MapGenerator.Generate(9));
        Assert.NotEqual(classicSolids, g.Map.Solids.Count);

        g.LoadClassicArena();
        Assert.Equal(classicSolids, g.Map.Solids.Count);
        Assert.Equal("Classic Arena", g.MapName);
        Assert.Equal(Arena.Half, g.MapHalf);
        Assert.Equal(Arena.Height, g.CeilingY);
        Assert.Equal(7, g.Pickups.Count(k => k.Kind == PickupKind.Weapon));
        Assert.Equal(4, g.JumpPads.Count);
    }

    [Fact]
    public void Player_and_pickups_work_on_a_random_map()
    {
        var g = Arena.Build(bots: 0);
        g.LoadMap(MapGenerator.Generate(21));
        var health = g.Pickups.First(k => k.Kind == PickupKind.Health);
        g.Player.Health = 50;
        g.Player.Move.Position = new Vector3(health.Position.X, 28, health.Position.Z);
        g.Tick(default, false);
        Assert.Equal(75, g.Player.Health);
        Assert.False(health.Active);
    }

    [Fact]
    public void Bots_fight_and_stay_inside_random_maps_of_any_size()
    {
        foreach (int seed in new[] { 3, 8, 14, 19 })
        {
            var g = Arena.Build(bots: 0);
            g.LoadMap(MapGenerator.Generate(seed));
            g.AddBot(); g.AddBot();
            g.Console.Execute("sv_cheats 1; god", echo: false);
            float half = g.MapHalf;
            for (int i = 0; i < 40 * (int)GameWorld.TickRate; i++)
            {
                g.Tick(new UserCmd { Forward = 1, Yaw = i * 0.7f }, false);
                foreach (var b in g.Bots)
                {
                    var p = b.Body.Move.Position;
                    Assert.InRange(p.X, -half - 5, half + 5); Assert.InRange(p.Z, -half - 5, half + 5);
                    Assert.InRange(p.Y, -10, g.CeilingY + 5);
                }
            }
        }
    }

    [Fact]
    public void Bots_wander_across_a_small_map_within_its_bounds()
    {
        MapData? small = null;
        foreach (int seed in Seeds) { var m = MapGenerator.Generate(seed); if (m.Half <= 1600f) { small = m; break; } }
        Assert.NotNull(small);
        var g = Arena.Build(bots: 0);
        g.LoadMap(small!);
        g.Console.Execute("sv_cheats 1; god", echo: false);
        var bot = g.AddBot();
        var cells = new HashSet<(int, int)>();
        for (int i = 0; i < 60 * (int)GameWorld.TickRate; i++)
        {
            g.Tick(default, false);
            var p = bot.Body.Move.Position;
            cells.Add(((int)MathF.Floor(p.X / 400), (int)MathF.Floor(p.Z / 400)));
        }
        Assert.True(cells.Count >= 4, $"bot only visited {cells.Count} cells on a {small!.Half * 2}-wide map");
    }

    // ---------------- console and menu ----------------

    [Fact]
    public void Console_map_command_loads_arena_or_random_maps()
    {
        var g = Arena.Build(bots: 0);
        g.Console.Execute("map");
        Assert.Contains(g.Console.Lines, l => l.Contains("current map: Classic Arena"));

        g.Console.Execute("map random 42");
        Assert.Equal("Random #42", g.MapName);
        Assert.Equal(42, g.MapSeed);
        Assert.Contains(g.Console.Lines, l => l.Contains("Random #42") && l.Contains("map random 42"));
        var solids = g.Map.Solids.Count;

        g.Console.Execute("map random 42");                      // same seed replays the same map
        Assert.Equal(solids, g.Map.Solids.Count);

        g.Console.Execute("map random");                         // fresh seed
        Assert.StartsWith("Random #", g.MapName);
        Assert.True(g.MapSeed > 0);

        g.Console.Execute("map arena");
        Assert.Equal("Classic Arena", g.MapName);

        g.Console.Execute("map random banana");
        Assert.Contains(g.Console.Lines, l => l.Contains("not a seed number"));
        g.Console.Execute("map wat");
        Assert.Contains(g.Console.Lines, l => l.Contains("usage: map"));
    }

    [Fact]
    public void Random_map_menu_item_generates_a_fresh_map_and_starts_the_game()
    {
        var g = Arena.Build(bots: 1);
        int nextSeed = 500;
        g.MapSeedSource = () => nextSeed++;
        bool started = false;
        var m = MenuModel.Create(g, () => started, () => started = true, () => { });

        Assert.Equal("RANDOM MAP", m.Current.Items[1].Label());
        m.SetSelected(1); m.Select();
        Assert.True(started);
        Assert.Equal("Random #500", g.MapName);
        Assert.Contains(g.Console.Lines, l => l.Contains("Random #500"));

        m.Select();                                               // again: a different seed, a different map
        Assert.Equal("Random #501", g.MapName);
        Assert.Single(g.Bots);                                    // bots carry over

        m.SetSelected(2); m.Select();
        Assert.Equal("Classic Arena", g.MapName);
    }

    [Fact]
    public void Fixed_arena_and_generated_maps_share_no_state()
    {
        var a = Arena.Build(bots: 0);
        var b = Arena.Build(bots: 0);
        a.LoadMap(MapGenerator.Generate(4));
        Assert.Equal("Classic Arena", b.MapName);                 // another game is unaffected
        Assert.NotEqual(a.Map.Solids.Count, b.Map.Solids.Count);
    }
}

static class GameWorldTestExtensions
{
    /// <summary>The platforms of the currently loaded generated map (regenerated from the seed, which is deterministic).</summary>
    public static IEnumerable<Platform> Platforms(this GameWorld g) => MapGenerator.Generate(g.MapSeed).Platforms;
}
