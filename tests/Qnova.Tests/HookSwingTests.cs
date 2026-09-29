using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class HookSwingTests
{
    /// <summary>Tall hall: floor at 0, ceiling at 1000. The player starts mid-air so a pendulum has room to swing.</summary>
    static GameWorld Hall()
    {
        var w = new World();
        w.Add(new(-4000, -64, -4000), new(4000, 0, 4000));
        w.Add(new(-4000, 1000, -4000), new(4000, 1064, 4000));
        var g = new GameWorld(w, new Vector3(0, 300, 0), seed: 4);
        g.Player.Move.Position = new Vector3(0, 300, 0);
        return g;
    }

    const float Pitch = 60f;    // hook the ceiling ~390 units ahead (-Z)

    static UserCmd Cmd(bool jump, bool grapple = true, float side = 0, float forward = 0) =>
        new() { Grapple = grapple, Jump = jump, Pitch = Pitch, Side = side, Forward = forward };

    /// <summary>Fire the hook (jump optionally held throughout) until it attaches; returns the anchor.</summary>
    static Vector3 Attach(GameWorld g, bool jump)
    {
        for (int i = 0; i < 2 * (int)GameWorld.TickRate && g.Player.Hook.State != HookState.Attached; i++) g.Tick(Cmd(jump), false);
        Assert.Equal(HookState.Attached, g.Player.Hook.State);
        return g.Player.Hook.Anchor;
    }

    static float DistToAnchor(GameWorld g, Vector3 anchor) => Vector3.Distance(g.Player.Move.Position, anchor);

    [Fact]
    public void Holding_jump_swings_on_a_fixed_length_rope_instead_of_reeling_in()
    {
        var g = Hall();
        var anchor = Attach(g, jump: true);
        Assert.InRange(anchor.Y, 998f, 1001f);
        float len = DistToAnchor(g, anchor);
        Assert.True(len > 500f, "rope should be long");

        float maxDist = 0, minY = float.MaxValue, maxSpeed = 0, minZ = float.MaxValue;
        for (int i = 0; i < 3 * (int)GameWorld.TickRate; i++)
        {
            g.Tick(Cmd(true), false);
            var m = g.Player.Move;
            maxDist = MathF.Max(maxDist, DistToAnchor(g, anchor));
            minY = MathF.Min(minY, m.Position.Y);
            minZ = MathF.Min(minZ, m.Position.Z);
            maxSpeed = MathF.Max(maxSpeed, m.Velocity.Length());
        }
        Assert.True(maxDist <= len + 8f, $"rope stretched: {maxDist} vs {len}");
        Assert.True(DistToAnchor(g, anchor) > len - 120f, "must not be reeled in while jump is held");
        Assert.True(maxSpeed > 250f, $"gravity should swing the player up to speed (got {maxSpeed})");
        Assert.True(minY > 28f, "swings above the floor");
        Assert.True(minZ < -200f, "swings toward the anchor's side");
    }

    [Fact]
    public void Without_jump_the_same_hook_reels_the_player_up_to_the_anchor()
    {
        var g = Hall();
        var anchor = Attach(g, jump: false);
        float len = DistToAnchor(g, anchor);
        for (int i = 0; i < 2 * (int)GameWorld.TickRate; i++) g.Tick(Cmd(false), false);
        Assert.True(DistToAnchor(g, anchor) < 120f, "reeled in");
        Assert.True(len > 500f);
    }

    [Fact]
    public void Letting_go_of_jump_resumes_the_reel_from_the_swing()
    {
        var g = Hall();
        var anchor = Attach(g, jump: true);
        float len = DistToAnchor(g, anchor);
        for (int i = 0; i < (int)GameWorld.TickRate; i++) g.Tick(Cmd(true), false);
        Assert.True(DistToAnchor(g, anchor) > len - 120f);

        for (int i = 0; i < (int)(1.6f * GameWorld.TickRate); i++) g.Tick(Cmd(false), false);
        Assert.True(DistToAnchor(g, anchor) < len - 250f, "reel resumed after releasing jump");
    }

    [Fact]
    public void Jump_can_be_pressed_again_to_swing_again()
    {
        var g = Hall();
        var anchor = Attach(g, jump: false);
        for (int i = 0; i < 10; i++) g.Tick(Cmd(false), false);          // reeling
        float len = DistToAnchor(g, anchor);
        for (int i = 0; i < (int)(1.5f * GameWorld.TickRate); i++) g.Tick(Cmd(true), false);
        float d = DistToAnchor(g, anchor);
        Assert.InRange(d, 60f, len + 8f);                                // captured the length when jump went down
        Assert.True(d > len - 200f);
    }

    [Fact]
    public void Steering_works_while_swinging_and_releasing_the_hook_keeps_the_swing_momentum()
    {
        float SideDrift(float side)
        {
            var g = Hall();
            var anchor = Attach(g, jump: true);
            for (int i = 0; i < 2 * (int)GameWorld.TickRate; i++) g.Tick(Cmd(true, side: side), false);
            return MathF.Abs(g.Player.Move.Position.X);
        }
        Assert.True(SideDrift(1f) > SideDrift(0f) + 40f, "strafing should bend the swing sideways");

        var g2 = Hall();
        Attach(g2, jump: true);
        for (int i = 0; i < (int)(1.2f * GameWorld.TickRate); i++) g2.Tick(Cmd(true), false);
        var v = g2.Player.Move.Velocity;
        Assert.True(v.Length() > 150f);
        g2.Tick(Cmd(true, grapple: false), false);                         // let go of the hook mid-swing
        Assert.Equal(HookState.None, g2.Player.Hook.State);
        Assert.True(Vector3.Distance(g2.Player.Move.Velocity, v) < 60f, "slingshot: keeps the swing velocity");
    }

    [Fact]
    public void Swinging_never_pushes_the_player_through_walls()
    {
        var g = Hall();
        g.Map.Add(new(-200, 0, -250), new(200, 900, -230));               // a wall in the swing path
        var anchor = Attach(g, jump: true);
        for (int i = 0; i < 4 * (int)GameWorld.TickRate; i++)
        {
            g.Tick(Cmd(true), false);
            var p = g.Player.Move.Position;
            Assert.False(p.Z < -230f + 16f - 0.5f && MathF.Abs(p.X) < 200f && p.Y < 900f && p.Z > -250f - 16f, $"inside the wall at {p}");
        }
        Assert.True(g.Map.IsEmpty(g.Player.Move.Position, MoveVars.Half) || g.Player.Hook.State != HookState.Attached);
    }

    [Fact]
    public void Jump_on_the_ground_without_a_hook_is_a_normal_jump()
    {
        var w = new World();
        w.Add(new(-4000, -64, -4000), new(4000, 0, 4000));
        var g = new GameWorld(w, new Vector3(0, 28, 0));
        g.Tick(default, false);
        g.Tick(new UserCmd { Jump = true }, false);
        Assert.True(g.Player.Move.Velocity.Y > 200f);
    }

    [Fact]
    public void Swing_state_resets_when_the_hook_is_dropped()
    {
        var g = Hall();
        var anchor = Attach(g, jump: true);
        for (int i = 0; i < 40; i++) g.Tick(Cmd(true), false);
        g.Tick(Cmd(false, grapple: false), false);                         // drop the hook
        for (int i = 0; i < 20; i++) g.Tick(Cmd(false, grapple: false), false);
        // fire again without jump: it must reel (i.e. no stale rope length)
        for (int i = 0; i < 3 * (int)GameWorld.TickRate; i++) g.Tick(Cmd(false), false);
        Assert.True(g.Player.Hook.State != HookState.Attached || DistToAnchor(g, g.Player.Hook.Anchor) < 150f);
    }
}
