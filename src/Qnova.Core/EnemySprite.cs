using System.Numerics;

namespace Qnova.Core;

/// <summary>Which picture of an enemy to show: seen from the front or back, and what it is doing.</summary>
public enum EnemyPose { Idle, WalkA, WalkB, Fire }

public readonly record struct EnemyFrame(bool Front, EnemyPose Pose);

public static class EnemySprite
{
    public const float WalkStride = 0.28f;      // seconds per step
    public const float FirePoseTime = 0.18f;    // how long the firing pose holds after a shot
    public const float MovingSpeed = 40f;       // flat speed above which it walks

    /// <summary>Doom-style sprite choice. <paramref name="botForward"/> is where the enemy faces; if that points toward the camera we see its front.</summary>
    public static EnemyFrame Pick(Vector3 botPos, Vector3 botForward, Vector3 camPos, float flatSpeed, float sinceShot, float time)
    {
        var toCam = new Vector3(camPos.X - botPos.X, 0, camPos.Z - botPos.Z);
        var fwd = new Vector3(botForward.X, 0, botForward.Z);
        bool front = toCam.LengthSquared() < 1e-6f || fwd.LengthSquared() < 1e-6f || Vector3.Dot(fwd, toCam) > 0;
        var pose = sinceShot < FirePoseTime ? EnemyPose.Fire
                 : flatSpeed > MovingSpeed ? (((int)MathF.Floor(time / WalkStride) & 1) == 0 ? EnemyPose.WalkA : EnemyPose.WalkB)
                 : EnemyPose.Idle;
        return new EnemyFrame(front, pose);
    }
}
