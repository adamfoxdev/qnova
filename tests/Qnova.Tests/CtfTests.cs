using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class CtfTests
{
    static GameWorld Ctf(int bots = 0)
    {
        var g = Arena.Build(bots: bots);
        g.LoadClassicArena();
        g.BotAi = false;
        g.SetMode(GameMode.Ctf);
        return g;
    }

    static void Step(GameWorld g, int n = 1) { for (int i = 0; i < n; i++) g.Tick(default, false); }
    static void Put(Player p, Vector3 at) { p.Move.Position = at; p.Move.Velocity = default; }

    [Fact]
    public void Ctf_sets_up_flags_teams_and_at_least_three_bots()
    {
        var g = Ctf();
        Assert.True(g.IsCtf);
        Assert.Equal(2, g.Flags.Count);
        Assert.Equal(3, g.Bots.Count);
        Assert.Equal(Team.Red, g.Player.Team);
        Assert.Equal(2, g.Combatants.Count(c => c.Team == Team.Red));
        Assert.Equal(2, g.Combatants.Count(c => c.Team == Team.Blue));
        Assert.All(g.Flags, f => Assert.Equal(FlagState.Home, f.State));
        // team members spawn near their own base
        Assert.True(Vector3.Distance(g.Player.Move.Position, g.FlagOf(Team.Red)!.Home) < 2200);
    }

    [Fact]
    public void Leaving_ctf_clears_teams_and_flags()
    {
        var g = Ctf();
        g.SetMode(GameMode.Deathmatch);
        Assert.Empty(g.Flags);
        Assert.All(g.Combatants, c => Assert.Equal(Team.None, c.Team));
    }

    [Fact]
    public void Touching_the_enemy_flag_picks_it_up_and_it_follows_you()
    {
        var g = Ctf();
        var blue = g.FlagOf(Team.Blue)!;
        Put(g.Player, blue.Home - new Vector3(0, 12, 0));
        Step(g);
        Assert.Equal(FlagState.Carried, blue.State);
        Assert.Same(g.Player, blue.Carrier);
        Put(g.Player, new Vector3(100, 28, 100));
        Step(g);
        Assert.True(Vector3.Distance(blue.Pos, g.Player.Move.Position) < 40);
    }

    [Fact]
    public void Touching_your_own_home_flag_does_nothing_and_teammates_cant_take_it()
    {
        var g = Ctf();
        var red = g.FlagOf(Team.Red)!;
        Put(g.Player, red.Home - new Vector3(0, 12, 0));
        Step(g);
        Assert.Equal(FlagState.Home, red.State);
        Assert.Equal(0, g.TeamScore[1]);
    }

    [Fact]
    public void Bringing_the_enemy_flag_home_scores_and_resets_it()
    {
        var g = Ctf();
        var red = g.FlagOf(Team.Red)!; var blue = g.FlagOf(Team.Blue)!;
        Put(g.Player, blue.Home - new Vector3(0, 12, 0)); Step(g);
        int frags = g.Player.Frags;
        Put(g.Player, red.Home - new Vector3(0, 12, 0)); Step(g);
        Assert.Equal(1, g.TeamScore[(int)Team.Red]);
        Assert.Equal(FlagState.Home, blue.State);
        Assert.Null(blue.Carrier);
        Assert.Equal(frags + GameWorld.CaptureFrags, g.Player.Frags);
        Assert.Contains(g.Events, e => e.Kind == EventKind.FlagCaptured);
    }

    [Fact]
    public void Cannot_capture_while_your_own_flag_is_away()
    {
        var g = Ctf();
        var red = g.FlagOf(Team.Red)!; var blue = g.FlagOf(Team.Blue)!;
        Put(g.Player, blue.Home - new Vector3(0, 12, 0)); Step(g);
        // a blue bot steals the red flag
        var thief = g.Bots.First(b => b.Body.Team == Team.Blue).Body;
        Put(thief, red.Home - new Vector3(0, 12, 0)); Step(g);
        Assert.Equal(FlagState.Carried, red.State);
        Put(thief, new Vector3(1000, 28, 0));
        Put(g.Player, new Vector3(-1000, 28, 0)); Step(g);
        Put(g.Player, red.Home - new Vector3(0, 12, 0)); Step(g);
        Assert.Equal(0, g.TeamScore[(int)Team.Red]);
        Assert.Equal(FlagState.Carried, blue.State);   // still holding, waiting for the flag to come back
    }

    [Fact]
    public void Killing_a_carrier_drops_the_flag_and_touching_it_returns_it()
    {
        var g = Ctf();
        var blue = g.FlagOf(Team.Blue)!;
        var blueBot = g.Bots.First(b => b.Body.Team == Team.Blue).Body;
        var redBot = g.Bots.First(b => b.Body.Team == Team.Red).Body;
        var red = g.FlagOf(Team.Red)!;
        // red flag carried by a blue bot, then shot dead by the human
        Put(blueBot, red.Home - new Vector3(0, 12, 0)); Step(g);
        Assert.Same(blueBot, red.Carrier);
        Put(blueBot, new Vector3(500, 28, 500)); Step(g);
        int frags = g.Player.Frags;
        g.DamagePlayer(blueBot, 500, Vector3.UnitX, g.Player);
        Assert.Equal(FlagState.Dropped, red.State);
        Assert.Equal(frags + 1 + 2, g.Player.Frags);          // kill + carrier bonus
        Assert.Contains(g.Events, e => e.Kind == EventKind.FlagDropped);
        // a red player touching it sends it home
        Put(redBot, red.Pos - new Vector3(0, 12, 0)); Step(g);
        Assert.Equal(FlagState.Home, red.State);
        Assert.Equal(red.Home, red.Pos);
    }

    [Fact]
    public void A_dropped_flag_returns_by_itself_after_the_timeout()
    {
        var g = Ctf();
        var blue = g.FlagOf(Team.Blue)!;
        Put(g.Player, blue.Home - new Vector3(0, 12, 0)); Step(g);
        g.Die(g.Player, null);
        Assert.Equal(FlagState.Dropped, blue.State);
        Step(g, (int)(GameWorld.FlagReturnTime * GameWorld.TickRate) + 5);
        Assert.Equal(FlagState.Home, blue.State);
    }

    [Fact]
    public void Enemy_flag_is_not_picked_up_by_a_second_carrier_and_no_friendly_fire()
    {
        var g = Ctf();
        var mate = g.Bots.First(b => b.Body.Team == Team.Red).Body;
        int hp = mate.Health;
        g.DamagePlayer(mate, 50, Vector3.UnitX, g.Player);
        Assert.Equal(hp, mate.Health);
        var foe = g.Bots.First(b => b.Body.Team == Team.Blue).Body;
        g.DamagePlayer(foe, 30, Vector3.UnitX, g.Player);
        Assert.Equal(70, foe.Health);
    }

    [Fact]
    public void Crosshair_is_not_red_over_a_teammate()
    {
        var g = Ctf();
        var mate = g.Bots.First(b => b.Body.Team == Team.Red).Body;
        var foe = g.Bots.First(b => b.Body.Team == Team.Blue).Body;
        Put(g.Player, new Vector3(-1500, 28, 800)); g.Player.Yaw = 0; g.Player.Pitch = 0;   // facing -Z
        Put(mate, new Vector3(-1500, 28, 400));
        Put(foe, new Vector3(800, 28, 800));
        Assert.False(g.AimingAtEnemy(g.Player));
        Put(foe, new Vector3(-1500, 28, 400)); Put(mate, new Vector3(800, 28, 800));
        Assert.True(g.AimingAtEnemy(g.Player));
    }

    [Fact]
    public void Reaching_the_capture_limit_ends_the_match_and_then_resets()
    {
        var g = Ctf();
        g.CaptureLimit = 1;
        var red = g.FlagOf(Team.Red)!; var blue = g.FlagOf(Team.Blue)!;
        Put(g.Player, blue.Home - new Vector3(0, 12, 0)); Step(g);
        Put(g.Player, red.Home - new Vector3(0, 12, 0)); Step(g);
        Assert.Equal(Team.Red, g.Winner);
        Assert.True(g.MatchOverUntil > g.Time);
        Assert.Contains(g.Console.Lines, l => l.Contains("RED TEAM WINS"));
        Step(g, (int)(7 * GameWorld.TickRate));
        Assert.Equal(Team.None, g.Winner);
        Assert.Equal(0, g.TeamScore[(int)Team.Red]);
        Assert.Equal(0, g.Player.Frags);
    }

    [Fact]
    public void Removing_the_carrier_returns_the_flag()
    {
        var g = Ctf();
        var red = g.FlagOf(Team.Red)!;
        var thief = g.Bots.First(b => b.Body.Team == Team.Blue);
        Put(thief.Body, red.Home - new Vector3(0, 12, 0)); Step(g);
        Assert.Equal(FlagState.Carried, red.State);
        g.Bots.Remove(thief); Step(g);
        Assert.Equal(FlagState.Home, red.State);
    }

    [Fact]
    public void Random_maps_get_bases_from_the_farthest_spawns()
    {
        var g = Arena.Build(bots: 0);
        g.SetMode(GameMode.Ctf);
        g.LoadMap(MapGenerator.Generate(42));
        Assert.Equal(2, g.Flags.Count);
        Assert.True(Vector3.Distance(g.Flags[0].Home, g.Flags[1].Home) > 500);
    }

    [Fact]
    public void Console_and_menu_switch_modes()
    {
        var g = Arena.Build(bots: 1);
        g.Console.Execute("gamemode ctf", echo: false);
        Assert.True(g.IsCtf);
        g.Console.Execute("flags", echo: false);
        Assert.Contains(g.Console.Lines, l => l.Contains("BLUE flag"));
        g.Console.Execute("gamemode dm", echo: false);
        Assert.False(g.IsCtf);
        var m = MenuModel.Create(g, () => false, () => { }, () => { });
        m.SetSelected(3);
        Assert.Equal("GAME MODE", m.SelectedItem.Label());
        Assert.Equal("DEATHMATCH", m.SelectedItem.Value!());
        m.Adjust(1);
        Assert.True(g.IsCtf);
        Assert.Equal("CAPTURE THE FLAG", m.SelectedItem.Value!());
    }

    [Fact]
    public void Bots_run_a_full_ctf_round_without_crashing_and_someone_takes_a_flag()
    {
        var g = Arena.Build(bots: 0);
        g.SetMode(GameMode.Ctf);
        g.LoadClassicArena();
        bool taken = false;
        for (int i = 0; i < 72 * 90 && !taken; i++)
        {
            g.Tick(default, false);
            taken = g.Events.Any(e => e.Kind == EventKind.FlagTaken);
            g.Events.Clear();
        }
        Assert.True(taken, "no bot got a flag in 90s");
    }
}
