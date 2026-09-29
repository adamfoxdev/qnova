using Qnova.Core;
using Raylib_cs;
using static ItemSprites;

/// <summary>Procedurally painted pixel-art soldiers for the bots: red and blue teams, front and back views,
/// two walking frames and a firing pose. No image assets.</summary>
sealed class EnemySprites : IDisposable
{
    readonly Dictionary<(Team, bool, EnemyPose), Texture2D> _tex = new();

    public EnemySprites()
    {
        foreach (var team in new[] { Team.Red, Team.Blue })
            foreach (bool front in new[] { true, false })
                foreach (EnemyPose pose in Enum.GetValues<EnemyPose>())
                    if (front || pose != EnemyPose.Fire)               // shooting away from you reads as the back idle pose
                        _tex[(team, front, pose)] = Upload(Paint(team, front, pose));
    }

    public Texture2D Get(Team team, EnemyFrame f)
    {
        var t = team == Team.Red ? Team.Red : Team.Blue;
        var pose = f.Front || f.Pose != EnemyPose.Fire ? f.Pose : EnemyPose.Idle;
        return _tex[(t, f.Front, pose)];
    }

    public void Dispose() { foreach (var t in _tex.Values) Raylib.UnloadTexture(t); }

    static Canvas Paint(Team team, bool front, EnemyPose pose)
    {
        var c = new Canvas();
        var armor = team == Team.Red ? C(196, 52, 44) : C(52, 104, 206);
        var dark = Lit(armor, 0.55f);
        var skin = C(214, 172, 138);
        var pants = C(58, 56, 62);
        var boots = C(34, 32, 36);
        var steel = C(88, 90, 100);

        // legs: the raised one is shorter and lifted
        int liftL = pose == EnemyPose.WalkA ? 5 : 0, liftR = pose == EnemyPose.WalkB ? 5 : 0;
        c.Shaded(22, 42, 9, 15 - liftL, pants); c.Shaded(21, 55 - liftL, 11, 7, boots);
        c.Shaded(33, 42, 9, 15 - liftR, pants); c.Shaded(32, 55 - liftR, 11, 7, boots);
        // torso and belt
        c.Shaded(19, 20, 26, 23, armor);
        c.Rect(19, 39, 26, 3, dark);
        if (front) { c.Rect(30, 21, 4, 17, dark); c.Rect(23, 24, 5, 3, Lit(armor, 1.4f)); c.Rect(36, 24, 5, 3, Lit(armor, 1.4f)); }
        else { c.Shaded(23, 22, 18, 16, dark); c.Rect(30, 22, 4, 16, Lit(dark, 0.8f)); }     // backpack
        // left arm hangs; right arm carries the gun
        c.Shaded(13, 21, 6, 19, armor); c.Rect(13, 38, 6, 4, skin);
        if (pose == EnemyPose.Fire && front)
        {
            c.Shaded(45, 15, 6, 17, armor); c.Rect(45, 30, 6, 4, skin);
            c.Shaded(44, 8, 8, 9, steel); c.Rect(46, 5, 4, 4, C(20, 20, 24));               // gun raised, barrel at us
            c.Disc(48, 5, 7, C(255, 230, 120)); c.Disc(48, 5, 4, C(255, 255, 240));        // muzzle flash
        }
        else
        {
            c.Shaded(45, 21, 6, 17, armor); c.Rect(45, 36, 6, 4, skin);
            c.Shaded(46, 30, 8, 9, steel); c.Rect(48, 38, 4, 3, C(20, 20, 24));
        }
        // head with helmet
        c.Shaded(23, 4, 18, 8, dark);
        c.Disc(32, 13, 8, skin);
        c.Shaded(23, 3, 18, 8, dark);
        if (front)
        {
            c.Rect(26, 11, 12, 4, C(28, 28, 32));               // visor
            c.Rect(27, 12, 3, 2, C(255, 210, 60)); c.Rect(34, 12, 3, 2, C(255, 210, 60));   // glowing eyes
            c.Rect(29, 17, 6, 2, Lit(skin, 0.7f));
        }
        else c.Shaded(24, 8, 16, 9, dark);
        c.Outline(C(14, 14, 18));
        return c;
    }
}
