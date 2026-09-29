using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class MovementTests
{
    static World Flat()
    {
        var w = new World();
        w.Add(new(-10000, -64, -10000), new(10000, 0, 10000));
        return w;
    }

    static float Flat2D(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    static PlayerMove Spawn(World w, Vector3? at = null)
    {
        var m = new PlayerMove(w) { Position = at ?? new Vector3(0, 28, 0) };
        m.Tick(default, GameWorld.Dt); // settle onto ground
        return m;
    }

    [Fact]
    public void Standing_player_stays_on_ground()
    {
        var m = Spawn(Flat());
        for (int i = 0; i < 100; i++) m.Tick(default, GameWorld.Dt);
        Assert.True(m.OnGround);
        Assert.InRange(m.Position.Y, 27.9f, 28.2f);
    }

    [Fact]
    public void Ground_speed_caps_at_maxspeed_and_friction_stops()
    {
        var m = Spawn(Flat());
        var run = new UserCmd { Forward = 1 };
        for (int i = 0; i < 200; i++) m.Tick(run, GameWorld.Dt);
        Assert.InRange(Flat2D(m.Velocity), 319f, 321f);
        Assert.True(m.Position.Z < -100); // yaw 0 faces -Z
        for (int i = 0; i < 200; i++) m.Tick(default, GameWorld.Dt);
        Assert.Equal(0f, Flat2D(m.Velocity));
    }

    [Fact]
    public void Jump_needs_release_between_presses()
    {
        var m = Spawn(Flat());
        var jump = new UserCmd { Jump = true };
        m.Tick(jump, GameWorld.Dt);
        Assert.False(m.OnGround);
        Assert.True(m.Velocity.Y > 250);
        // hold through landing; should not re-jump
        float maxY = 0;
        for (int i = 0; i < 200; i++) { m.Tick(jump, GameWorld.Dt); maxY = Math.Max(maxY, m.Position.Y); }
        Assert.True(m.OnGround);
        Assert.InRange(maxY, 28 + 40, 28 + 50); // apex ~ 270^2/(2*800) = 45.6
    }

    [Fact]
    public void Strafe_jumping_exceeds_maxspeed()
    {
        var m = Spawn(Flat());
        var run = new UserCmd { Forward = 1 };
        for (int i = 0; i < 100; i++) m.Tick(run, GameWorld.Dt);
        float start = Flat2D(m.Velocity);

        // Air-strafe: hold forward+left and keep turning left so wishdir stays ~45-90 deg off velocity; re-jump on landing.
        float yaw = 0;
        for (int i = 0; i < 720; i++)
        {
            bool grounded = m.OnGround;
            if (!grounded) yaw += 1.6f;
            m.Tick(new UserCmd { Forward = 1, Side = -1, Jump = grounded, Yaw = yaw }, GameWorld.Dt);
        }
        float end = Flat2D(m.Velocity);
        Assert.True(end > start * 1.3f, $"strafe-jump speed {end} should be well above ground speed {start}");
    }

    [Fact]
    public void Air_control_never_adds_speed_when_already_above_cap_along_wishdir()
    {
        var m = Spawn(Flat());
        m.Velocity = new Vector3(0, 100, -400); m.OnGround = false;
        m.Position += new Vector3(0, 30, 0);
        // pushing forward along velocity gains nothing
        m.Tick(new UserCmd { Forward = 1 }, GameWorld.Dt);
        Assert.InRange(Flat2D(m.Velocity), 399f, 401f);
    }

    [Fact]
    public void Walls_stop_and_slide()
    {
        var w = Flat();
        w.Add(new(-100, 0, -300), new(100, 200, -280));
        var m = Spawn(w);
        var run = new UserCmd { Forward = 1 };
        for (int i = 0; i < 300; i++) m.Tick(run, GameWorld.Dt);
        Assert.True(m.Position.Z >= -280 + 16 - 0.01f);   // did not tunnel
        Assert.True(m.OnGround);
    }

    [Fact]
    public void Diagonal_into_wall_slides_along_it()
    {
        var w = Flat();
        w.Add(new(-10000, 0, -300), new(10000, 200, -280));
        var m = Spawn(w);
        var cmd = new UserCmd { Forward = 1, Side = 1 };  // up-right diagonal
        for (int i = 0; i < 300; i++) m.Tick(cmd, GameWorld.Dt);
        Assert.True(m.Position.X > 300, "should slide right along the wall");
    }

    [Fact]
    public void Steps_up_small_ledges_but_not_big_ones()
    {
        var w = Flat();
        w.Add(new(-100, 0, -400), new(100, 16, -300));     // 16 high: climbable
        w.Add(new(-100, 0, -800), new(100, 40, -700));     // 40 high: wall
        var m = Spawn(w);
        var run = new UserCmd { Forward = 1 };
        for (int i = 0; i < 100; i++) m.Tick(run, GameWorld.Dt);
        Assert.InRange(m.Position.Y, 28 + 15, 28 + 17);   // standing on the step
        for (int i = 0; i < 300; i++) m.Tick(run, GameWorld.Dt);
        Assert.True(m.Position.Z > -700 + 16 - 0.1f, "blocked by tall ledge");
        Assert.True(m.Position.Z < -700 + 16 + 40);
    }

    [Fact]
    public void Trace_hits_floor_with_up_normal()
    {
        var w = Flat();
        var t = w.TraceBox(new Vector3(0, 100, 0), new Vector3(0, -100, 0), MoveVars.Half);
        Assert.True(t.Hit);
        Assert.Equal(Vector3.UnitY, t.Normal);
        Assert.InRange(t.EndPos.Y, 28f, 28.1f);
    }
}
