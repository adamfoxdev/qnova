using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class EnemySpriteTests
{
    static readonly Vector3 Bot = new(0, 28, 0);

    [Fact]
    public void Facing_the_camera_shows_the_front_and_facing_away_shows_the_back()
    {
        var cam = new Vector3(0, 50, 500);
        Assert.True(EnemySprite.Pick(Bot, new Vector3(0, 0, 1), cam, 0, 10, 0).Front);      // looking toward the camera (+Z)
        Assert.False(EnemySprite.Pick(Bot, new Vector3(0, 0, -1), cam, 0, 10, 0).Front);    // walking away
    }

    [Fact]
    public void Camera_height_and_the_bots_pitch_do_not_change_the_facing()
    {
        var cam = new Vector3(0, 900, 500);
        Assert.True(EnemySprite.Pick(Bot, new Vector3(0, -0.9f, 0.2f), cam, 0, 10, 0).Front);
        Assert.True(EnemySprite.Pick(Bot, new Vector3(0, 0, 0), cam, 0, 10, 0).Front);      // degenerate: default to the front
    }

    [Fact]
    public void Pose_is_idle_standing_still_and_alternates_walk_frames_while_moving()
    {
        var cam = new Vector3(0, 50, 500); var fwd = new Vector3(0, 0, 1);
        Assert.Equal(EnemyPose.Idle, EnemySprite.Pick(Bot, fwd, cam, 0, 10, 3f).Pose);
        var a = EnemySprite.Pick(Bot, fwd, cam, 250, 10, 0.0f).Pose;
        var b = EnemySprite.Pick(Bot, fwd, cam, 250, 10, EnemySprite.WalkStride + 0.01f).Pose;
        Assert.Equal(EnemyPose.WalkA, a); Assert.Equal(EnemyPose.WalkB, b);
    }

    [Fact]
    public void Firing_overrides_walking_for_a_short_moment()
    {
        var cam = new Vector3(0, 50, 500); var fwd = new Vector3(0, 0, 1);
        Assert.Equal(EnemyPose.Fire, EnemySprite.Pick(Bot, fwd, cam, 250, 0.05f, 0).Pose);
        Assert.NotEqual(EnemyPose.Fire, EnemySprite.Pick(Bot, fwd, cam, 250, EnemySprite.FirePoseTime + 0.01f, 0).Pose);
    }

    [Fact]
    public void Shooting_records_the_time_for_the_firing_pose()
    {
        var g = Arena.Build(bots: 0);
        Assert.True(g.Time - g.Player.LastFire > 1f);
        g.Tick(default, fire: true);
        Assert.True(g.Time - g.Player.LastFire < EnemySprite.FirePoseTime);
    }
}
