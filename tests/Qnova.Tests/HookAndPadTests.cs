using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class HookAndPadTests
{
    /// <summary>Flat floor with a big wall 1000 units ahead (yaw 0 faces -Z).</summary>
    static GameWorld WithWall(float wallZ = -1000)
    {
        var w = new World();
        w.Add(new(-4000, -64, -4000), new(4000, 0, 4000));
        w.Add(new(-600, 0, wallZ - 100), new(600, 700, wallZ));
        var g = new GameWorld(w, new Vector3(0, 28, 0), seed: 9);
        g.SpawnPoints.Add(new Vector3(0, 28, 0));
        g.Tick(default, false);
        return g;
    }

    static GameWorld Open()
    {
        var w = new World();
        w.Add(new(-4000, -64, -4000), new(4000, 0, 4000));
        var g = new GameWorld(w, new Vector3(0, 28, 0), seed: 9);
        g.Tick(default, false);
        return g;
    }

    static void Hold(GameWorld g, float seconds, bool grapple = true, float yaw = 0, float pitch = 0)
    {
        for (int i = 0; i < (int)(seconds * GameWorld.TickRate); i++) g.Tick(new UserCmd { Grapple = grapple, Yaw = yaw, Pitch = pitch }, false);
    }

    // ---------------- grappling hook ----------------

    [Fact]
    public void Grapple_is_bound_to_F_by_default()
    {
        Assert.Equal("F", new KeyBindings().Get(InputAction.Grapple));
        Assert.Equal("GRAPPLING HOOK", KeyBindings.Label(InputAction.Grapple));
    }

    [Fact]
    public void Hook_flies_attaches_to_the_wall_and_reels_the_player_in()
    {
        var g = WithWall();
        Hold(g, 0.1f);                                            // ~ 260 units of flight per 0.1s at 2600 u/s
        Assert.Equal(HookState.Flying, g.Player.Hook.State);
        Assert.Contains(g.Events, e => e.Kind == EventKind.HookFire);
        Hold(g, 0.4f);
        Assert.Equal(HookState.Attached, g.Player.Hook.State);
        Assert.Contains(g.Events, e => e.Kind == EventKind.HookAttach);
        Assert.InRange(g.Player.Hook.Anchor.Z, -1001f, -999f);    // stuck to the wall face at z=-1000

        float z0 = g.Player.Move.Position.Z;
        Hold(g, 0.5f);
        Assert.True(g.Player.Move.Position.Z < z0 - 200f, "should be reeled toward the wall");
        float speed = MathF.Abs(g.Player.Move.Velocity.Z);
        Assert.InRange(speed, 500f, 810f);                        // eased up toward sv_hookspeed 800
        Assert.False(g.Player.Move.OnGround && g.Player.Move.Velocity.Y > 0);
    }

    [Fact]
    public void Releasing_keeps_momentum_and_gravity_returns()
    {
        var g = WithWall();
        g.Player.Move.Position = new Vector3(0, 300, 0);         // in the air so we can watch the fall
        Hold(g, 0.8f);
        Assert.Equal(HookState.Attached, g.Player.Hook.State);
        float vz = g.Player.Move.Velocity.Z;
        Assert.True(vz < -300f);

        g.Tick(new UserCmd { Grapple = false }, false);           // let go
        Assert.Equal(HookState.None, g.Player.Hook.State);
        Assert.Null(g.Player.Move.HookAnchor);
        Assert.InRange(g.Player.Move.Velocity.Z, vz - 5f, vz + 5f);   // slingshot: velocity kept
        float y = g.Player.Move.Velocity.Y;
        Hold(g, 0.2f, grapple: false);
        Assert.True(g.Player.Move.Velocity.Y < y - 100f, "gravity should be back");
    }

    [Fact]
    public void Hook_range_is_limited()
    {
        var g = Open();
        Hold(g, 2.0f);                                            // nothing to hit within range
        Assert.Equal(HookState.None, g.Player.Hook.State);
        Assert.Null(g.Player.Move.HookAnchor);

        var g2 = WithWall(-1000);
        g2.Console.Execute("sv_hookrange 300", echo: false);      // wall is 1000 away
        Hold(g2, 1.0f);
        Assert.Equal(HookState.None, g2.Player.Hook.State);
        Assert.DoesNotContain(g2.Events, e => e.Kind == EventKind.HookAttach);
    }

    [Fact]
    public void Hook_does_not_grab_dummies_or_bots()
    {
        var g = WithWall();
        g.Targets.Add(new Target { Origin = new Vector3(0, 28, -400) });
        Hold(g, 0.6f);
        Assert.Equal(HookState.None, g.Player.Hook.State);
        Assert.DoesNotContain(g.Events, e => e.Kind == EventKind.HookAttach);

        var g2 = WithWall();
        var bot = g2.AddBot();
        g2.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(0, 28, -400);
        Hold(g2, 0.6f);
        Assert.Equal(HookState.None, g2.Player.Hook.State);
    }

    [Fact]
    public void Hook_attaches_to_the_ceiling_and_floor_too()
    {
        var w = new World();
        w.Add(new(-4000, -64, -4000), new(4000, 0, 4000));
        w.Add(new(-4000, 500, -4000), new(4000, 564, 4000));
        var g = new GameWorld(w, new Vector3(0, 28, 0));
        g.Tick(default, false);
        Hold(g, 0.5f, pitch: 90);                                  // straight up
        Assert.Equal(HookState.Attached, g.Player.Hook.State);
        Assert.InRange(g.Player.Hook.Anchor.Y, 498f, 501f);
        Hold(g, 0.6f, pitch: 90);
        Assert.True(g.Player.Move.Position.Y > 150f, "reeled up toward the ceiling");
    }

    [Fact]
    public void Dying_or_respawning_drops_the_hook()
    {
        var g = WithWall();
        Hold(g, 0.5f);
        Assert.Equal(HookState.Attached, g.Player.Hook.State);
        g.Console.Execute("kill");
        g.Tick(new UserCmd { Grapple = true }, false);
        Assert.Equal(HookState.None, g.Player.Hook.State);
        Assert.Null(g.Player.Move.HookAnchor);

        var g2 = WithWall();
        Hold(g2, 0.5f);
        g2.Respawn();
        Assert.Equal(HookState.None, g2.Player.Hook.State);
        Assert.Null(g2.Player.Move.HookAnchor);
    }

    [Fact]
    public void Holding_the_key_does_not_refire_and_a_new_press_is_needed()
    {
        var g = WithWall();
        Hold(g, 0.5f);
        Assert.Equal(HookState.Attached, g.Player.Hook.State);
        g.Tick(new UserCmd { Grapple = false }, false);           // release
        int fires = g.Events.Count(e => e.Kind == EventKind.HookFire);
        g.Tick(new UserCmd { Grapple = true }, false);            // immediate re-press: on cooldown
        Assert.Equal(HookState.None, g.Player.Hook.State);
        Assert.Equal(fires, g.Events.Count(e => e.Kind == EventKind.HookFire));

        Hold(g, 0.4f, grapple: false);                            // cooldown over
        g.Tick(new UserCmd { Grapple = true }, false);
        Assert.Equal(HookState.Flying, g.Player.Hook.State);
    }

    [Fact]
    public void Hook_lets_go_when_the_player_is_snagged_and_not_making_progress()
    {
        var g = WithWall();
        Hold(g, 0.5f);
        Assert.Equal(HookState.Attached, g.Player.Hook.State);
        // a new obstacle slams down between the player and the anchor
        g.Map.Add(new(-300, 0, -600), new(300, 400, -560));
        for (int i = 0; i < 3 * (int)GameWorld.TickRate && g.Player.Hook.State == HookState.Attached; i++)
            g.Tick(new UserCmd { Grapple = true }, false);
        Assert.Equal(HookState.None, g.Player.Hook.State);
        Assert.True(g.Player.Move.Position.Z > -560f, "did not tunnel through the obstacle");
    }

    [Fact]
    public void Hook_speed_cvar_changes_the_pull()
    {
        float Peak(string cfg)
        {
            var g = WithWall(-3000);
            g.Console.Execute("sv_hookrange 4000", echo: false);   // the wall is beyond the default range
            g.Console.Execute(cfg, echo: false);
            float peak = 0;
            for (int i = 0; i < 2 * (int)GameWorld.TickRate; i++)
            {
                g.Tick(new UserCmd { Grapple = true }, false);
                peak = MathF.Max(peak, MathF.Abs(g.Player.Move.Velocity.Z));
            }
            return peak;
        }
        Assert.True(Peak("sv_hookspeed 1600") > Peak("sv_hookspeed 400") * 2.5f);
    }

    // ---------------- jump pads ----------------

    [Fact]
    public void Launch_velocity_puts_the_apex_at_the_target()
    {
        var pad = new JumpPad { Trigger = new Aabb(new(-48, 0, -48), new(48, 40, 48)), Target = new Vector3(400, 300, -200) };
        var from = new Vector3(0, 28, 0);
        const float gr = 800f;
        var v = pad.LaunchVelocity(from, gr);
        float tApex = v.Y / gr;
        var apex = from + new Vector3(v.X * tApex, v.Y * tApex - 0.5f * gr * tApex * tApex, v.Z * tApex);
        Assert.InRange(apex.X, 399.5f, 400.5f);
        Assert.InRange(apex.Y, 299.5f, 300.5f);
        Assert.InRange(apex.Z, -200.5f, -199.5f);

        // lower gravity: still peaks at the target, just with a slower, floatier flight
        var v2 = pad.LaunchVelocity(from, 400f);
        Assert.True(v2.Y < v.Y);
        Assert.True(MathF.Abs(v2.X) < MathF.Abs(v.X));
    }

    [Fact]
    public void Target_at_or_below_the_pad_still_launches_upward()
    {
        var pad = new JumpPad { Trigger = new Aabb(new(-48, 0, -48), new(48, 40, 48)), Target = new Vector3(300, 0, 0) };
        var v = pad.LaunchVelocity(new Vector3(0, 28, 0), 800f);
        Assert.True(v.Y > 100f && float.IsFinite(v.X));
    }

    [Fact]
    public void Stepping_on_a_pad_launches_you_once_then_respects_the_cooldown()
    {
        var g = Open();
        g.JumpPads.Add(new JumpPad { Trigger = new Aabb(new(-48, 0, -48), new(48, 40, 48)), Target = new Vector3(500, 350, 0) });
        g.Tick(default, false);
        Assert.False(g.Player.Move.OnGround);
        Assert.True(g.Player.Move.Velocity.Y > 300f);
        Assert.Single(g.Events, e => e.Kind == EventKind.JumpPad);
        Assert.Contains(g.Events, e => e.Kind == EventKind.JumpPad && e.B.X == 1);

        // apex is at the target
        float maxY = 0; Vector3 at = default;
        for (int i = 0; i < 3 * (int)GameWorld.TickRate; i++)
        {
            g.Tick(default, false);
            if (g.Player.Move.Position.Y > maxY) { maxY = g.Player.Move.Position.Y; at = g.Player.Move.Position; }
        }
        Assert.InRange(maxY, 340f, 360f);
        Assert.InRange(at.X, 470f, 530f);
    }

    [Fact]
    public void Dead_players_are_not_launched_and_gravity_cvar_still_hits_the_target_height()
    {
        var g = Open();
        g.Console.Execute("kill");
        g.JumpPads.Add(new JumpPad { Trigger = new Aabb(new(-48, 0, -48), new(48, 40, 48)), Target = new Vector3(0, 300, 200) });
        g.Tick(default, false);
        Assert.DoesNotContain(g.Events, e => e.Kind == EventKind.JumpPad);

        var g2 = Open();
        g2.Console.Execute("sv_gravity 400", echo: false);
        g2.JumpPads.Add(new JumpPad { Trigger = new Aabb(new(-48, 0, -48), new(48, 40, 48)), Target = new Vector3(0, 300, 200) });
        float maxY = 0;
        for (int i = 0; i < 4 * (int)GameWorld.TickRate; i++) { g2.Tick(default, false); maxY = MathF.Max(maxY, g2.Player.Move.Position.Y); }
        Assert.InRange(maxY, 290f, 310f);
    }

    [Fact]
    public void Bots_get_launched_by_pads_too()
    {
        var g = Open();
        var bot = g.AddBot();
        g.Console.Execute("bot_ai 0", echo: false);
        bot.Body.Move.Position = new Vector3(600, 28, 600);
        g.JumpPads.Add(new JumpPad { Trigger = new Aabb(new(552, 0, 552), new(648, 40, 648)), Target = new Vector3(900, 300, 600) });
        g.Tick(default, false);
        Assert.True(bot.Body.Move.Velocity.Y > 300f);
        Assert.Contains(g.Events, e => e.Kind == EventKind.JumpPad && e.B.X == 0);
    }

    [Fact]
    public void Every_arena_pad_flies_its_arc_to_the_apex_and_lands_safely()
    {
        var probe = Arena.Build(bots: 0);
        Assert.True(probe.JumpPads.Count >= 4);
        for (int idx = 0; idx < probe.JumpPads.Count; idx++)
        {
            var g = Arena.Build(bots: 0);
            var pad = g.JumpPads[idx];
            g.Player.Move.Position = new Vector3(pad.Center.X, pad.Trigger.Min.Y + 28f, pad.Center.Z);
            g.Tick(default, false);
            Assert.Contains(g.Events, e => e.Kind == EventKind.JumpPad);

            float maxY = float.MinValue; Vector3 apexAt = default; bool landed = false;
            for (int i = 0; i < 8 * (int)GameWorld.TickRate; i++)
            {
                g.Tick(default, false);
                var p = g.Player.Move.Position;
                Assert.InRange(p.X, -2050, 2050); Assert.InRange(p.Z, -2050, 2050); Assert.InRange(p.Y, -5, 760);
                if (p.Y > maxY) { maxY = p.Y; apexAt = p; }
                if (i > 20 && g.Player.Move.OnGround) { landed = true; break; }
            }
            var t = pad.Target;
            Assert.True(Vector3.Distance(apexAt, t) < 60f, $"pad {idx}: apex {apexAt} should be near target {t}");
            Assert.True(landed, $"pad {idx}: never landed");
        }
    }

    [Fact]
    public void Arena_pads_have_glowing_plates_and_lights_and_sit_on_open_floor()
    {
        var g = Arena.Build(bots: 0);
        foreach (var pad in g.JumpPads)
        {
            var standing = new Vector3(pad.Center.X, pad.Trigger.Min.Y + 28f, pad.Center.Z);
            Assert.True(g.Map.IsEmpty(standing, MoveVars.Half), $"pad at {pad.Center} is inside geometry");
            Assert.Contains(g.Decor, d => d.Surface == Surface.Emissive && MathF.Abs(d.Box.Center.X - pad.Center.X) < 1f && MathF.Abs(d.Box.Center.Z - pad.Center.Z) < 1f);
            Assert.Contains(g.Lights, l => MathF.Abs(l.Position.X - pad.Center.X) < 1f && MathF.Abs(l.Position.Z - pad.Center.Z) < 1f && l.Color.Z > 1f);
        }
    }
}
