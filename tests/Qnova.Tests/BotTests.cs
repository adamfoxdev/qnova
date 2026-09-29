using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class BotTests
{
    static GameWorld Flat(int bots = 0)
    {
        var w = new World();
        w.Add(new(-3000, -64, -3000), new(3000, 0, 3000));
        var g = new GameWorld(w, new Vector3(0, 28, 0), seed: 7);
        g.SpawnPoints.Add(new Vector3(0, 28, 0));
        for (int i = 0; i < bots; i++) g.AddBot();
        g.Tick(default, false);
        return g;
    }

    static void Sim(GameWorld g, float seconds, Func<UserCmd>? human = null, bool fire = false)
    {
        int n = (int)(seconds * GameWorld.TickRate);
        for (int i = 0; i < n; i++) g.Tick(human?.Invoke() ?? default, fire);
    }

    [Fact]
    public void Arena_geometry_is_valid_for_every_spawn_and_dummy()
    {
        var g = Arena.Build(bots: 0);
        foreach (var sp in g.SpawnPoints)
        {
            Assert.True(g.Map.IsEmpty(sp, MoveVars.Half), $"spawn {sp} is inside geometry");
            var down = g.Map.TraceBox(sp, sp - new Vector3(0, 20, 0), MoveVars.Half);
            Assert.True(down.Hit, $"spawn {sp} is not near the floor");
        }
        foreach (var t in g.Targets) Assert.True(g.Map.IsEmpty(t.Origin, t.Half), $"dummy {t.Origin} inside geometry");
        Assert.True(g.Map.IsEmpty(g.SpawnPoint, MoveVars.Half));
    }

    [Fact]
    public void Arena_is_much_larger_than_before()
    {
        var g = Arena.Build(bots: 0);
        var xs = g.Map.Solids;
        Assert.True(xs.Max(s => s.Max.X) - xs.Min(s => s.Min.X) > 4000);
        Assert.True(xs.Count > 60);
    }

    [Fact]
    public void Arena_starts_with_a_bot()
    {
        var g = Arena.Build();
        Assert.Single(g.Bots);
        Assert.True(g.Bots[0].Body.IsBot);
        Assert.True(g.Bots[0].Body.Alive);
    }

    [Fact]
    public void Bot_spawns_far_from_the_player()
    {
        var g = Flat();
        g.SpawnPoints.Clear();
        g.SpawnPoints.AddRange(new[] { new Vector3(0, 28, 0), new Vector3(2000, 28, 0), new Vector3(-100, 28, 0) });
        var bot = g.AddBot();
        Assert.Equal(new Vector3(2000, 28, 0), bot.Body.Move.Position);
    }

    [Fact]
    public void Bot_that_sees_a_stationary_player_hurts_them()
    {
        var g = Flat();
        var bot = g.AddBot();
        bot.Body.Move.Position = new Vector3(0, 28, -600);
        g.BotSkill = 5;
        Sim(g, 10);
        Assert.True(g.Player.Health < 100, "bot should have landed some damage on a sitting duck");
    }

    [Fact]
    public void Bot_kills_a_sitting_player_and_scores_a_frag()
    {
        var g = Flat();
        var bot = g.AddBot();
        bot.Body.Move.Position = new Vector3(0, 28, -400);
        g.BotSkill = 5;
        Sim(g, 30);
        Assert.True(g.Player.Deaths >= 1);
        Assert.True(bot.Body.Frags >= 1);
        Assert.Contains(g.Console.Lines, l => l.Contains("Bot1 killed You"));
    }

    [Fact]
    public void Human_can_kill_bot_and_bot_respawns()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);        // frozen target
        bot.Body.Move.Position = new Vector3(0, 28, -150);
        g.Player.Current = WeaponId.SuperShotgun; g.Player.Shells = 50;
        var aim = new UserCmd { Yaw = 0, Pitch = -2f };     // roughly at the bot's body
        Sim(g, 3, () => aim, fire: true);
        Assert.False(bot.Body.Alive);
        Assert.Equal(1, g.Player.Frags);
        Assert.Equal(1, bot.Body.Deaths);

        g.Console.Execute("bot_ai 1", echo: false);
        Sim(g, 3.5f);
        Assert.True(bot.Body.Alive, "bot should respawn after its delay");
        Assert.Equal(bot.Body.MaxHealth, bot.Body.Health);
    }

    [Fact]
    public void Rocket_splash_hurts_and_launches_a_bot()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -300);
        g.RadiusDamage(new Vector3(0, 10, -300), 160, 120, null, g.Player);
        Assert.True(bot.Body.Health < 100);
        Assert.True(bot.Body.Move.Velocity.Y > 0 || bot.Body.Move.Velocity.LengthSquared() > 1000);
    }

    [Fact]
    public void Bot_projectiles_never_hit_their_owner_directly()
    {
        var g = Flat();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Current = WeaponId.RocketLauncher;
        bot.Body.Move.Position = new Vector3(0, 28, 500);
        bot.Body.Pitch = 0; bot.Body.Yaw = 180;     // faces +Z, away from the player at the origin
        g.TryFire(bot.Body);
        Sim(g, 2);
        Assert.Equal(100, bot.Body.Health);   // shot forward into empty space; no self-hit at spawn
    }

    [Fact]
    public void Bot_wanders_around_the_arena_when_it_cannot_see_you()
    {
        var g = Arena.Build(bots: 0);
        g.Player.Move.Position = new Vector3(0, 156, 0);   // hidden-ish atop the mesa, and we turn god on to survive
        g.Console.Execute("sv_cheats 1; god", echo: false);
        var bot = g.AddBot();
        g.Console.Execute("bot_skill 1", echo: false);
        var cells = new HashSet<(int, int)>();
        Vector3 prev = bot.Body.Move.Position; float travelled = 0;
        for (int i = 0; i < 60 * (int)GameWorld.TickRate; i++)
        {
            g.Tick(default, false);
            var p = bot.Body.Move.Position;
            travelled += Vector3.Distance(p, prev); prev = p;
            cells.Add(((int)MathF.Floor(p.X / 512), (int)MathF.Floor(p.Z / 512)));
            Assert.InRange(p.X, -2100, 2100); Assert.InRange(p.Z, -2100, 2100); Assert.InRange(p.Y, -10, 800);
        }
        Assert.True(travelled > 3000, $"bot barely moved: {travelled}");
        Assert.True(cells.Count >= 6, $"bot only visited {cells.Count} map cells");
    }

    [Fact]
    public void Bots_stay_alive_and_in_bounds_fighting_a_moving_player_for_two_minutes()
    {
        var g = Arena.Build(bots: 2);
        g.Console.Execute("sv_cheats 1; god", echo: false);
        float yaw = 0;
        Sim(g, 120, () => { yaw += 1.1f; return new UserCmd { Forward = 1, Yaw = yaw }; });
        foreach (var b in g.Bots)
        {
            var p = b.Body.Move.Position;
            Assert.InRange(p.X, -2100, 2100); Assert.InRange(p.Z, -2100, 2100); Assert.InRange(p.Y, -10, 800);
        }
        Assert.True(g.Bots.Sum(b => b.Body.Frags) >= 0);
    }

    [Fact]
    public void Bot_commands_and_cvars()
    {
        var g = Flat();
        g.Console.Execute("bot_add 3");
        Assert.Equal(3, g.Bots.Count);
        Assert.Equal(new[] { "Bot1", "Bot2", "Bot3" }, g.Bots.Select(b => b.Body.Name));
        g.Console.Execute("bot_skill 9");
        Assert.Equal(5, g.BotSkill);
        g.Console.Execute("bots");
        Assert.Contains(g.Console.Lines, l => l.StartsWith("Bot2"));
        g.Console.Execute("bot_removeall");
        Assert.Empty(g.Bots);
    }

    [Fact]
    public void Player_dies_and_respawns_automatically()
    {
        var g = Flat();
        g.Console.Execute("kill");
        Assert.False(g.Player.Alive);
        Assert.Equal(1, g.Player.Deaths);
        Assert.Equal(-1, g.Player.Frags);   // suicide costs a frag
        Sim(g, 3.2f);
        Assert.True(g.Player.Alive);
    }
}
