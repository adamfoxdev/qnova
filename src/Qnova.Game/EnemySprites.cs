using Qnova.Core;
using Raylib_cs;
using static ItemSprites;

/// <summary>Procedurally painted pixel-art ogres for the bots: red and blue teams, front and back views,
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

    /// <summary>Dev aid (--sprite-sheet): every frame for both teams on one PNG, 4x, so the art can be checked without playing.</summary>
    public static void WriteContactSheet(string path)
    {
        const int Z = 4, S = ItemSprites.Size;
        var frames = new (bool Front, EnemyPose Pose)[] { (true, EnemyPose.Idle), (true, EnemyPose.WalkA), (true, EnemyPose.WalkB), (true, EnemyPose.Fire), (false, EnemyPose.Idle), (false, EnemyPose.WalkA), (false, EnemyPose.WalkB) };
        var img = Raylib.GenImageColor(frames.Length * S * Z, 2 * S * Z, new Color(52, 46, 44, 255));
        int row = 0;
        foreach (var team in new[] { Team.Red, Team.Blue })
        {
            for (int fi = 0; fi < frames.Length; fi++)
            {
                var cv = Paint(team, frames[fi].Front, frames[fi].Pose);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        var px = cv.Px[y * S + x]; if (px.A == 0) continue;
                        for (int dy = 0; dy < Z; dy++) for (int dx = 0; dx < Z; dx++) Raylib.ImageDrawPixel(ref img, fi * S * Z + x * Z + dx, row * S * Z + y * Z + dy, px);
                    }
            }
            row++;
        }
        Raylib.ExportImage(img, path); Raylib.UnloadImage(img);
    }

    static Canvas Paint(Team team, bool front, EnemyPose pose)
    {
        // An ogre-style brute: hunched, barrel-chested, a small head between spiked pauldrons in the team colour, carrying a grenade launcher.
        var c = new Canvas();
        var team_ = team == Team.Red ? C(206, 46, 38) : C(52, 104, 220);
        var skin = C(150, 116, 88); var skinD = Lit(skin, 0.68f); var skinL = Lit(skin, 1.22f);
        var leather = C(78, 54, 34); var leatherD = Lit(leather, 0.6f);
        var iron = C(96, 98, 108); var ironD = Lit(iron, 0.6f);
        var bone = C(228, 220, 196);

        // legs: thick; the raised one lifts while walking
        int liftL = pose == EnemyPose.WalkA ? 6 : 0, liftR = pose == EnemyPose.WalkB ? 6 : 0;
        c.Shaded(20, 47, 10, 11 - liftL, skinD); c.Shaded(18, 56 - liftL, 13, 6, leatherD);
        c.Shaded(34, 47, 10, 11 - liftR, skinD); c.Shaded(33, 56 - liftR, 13, 6, leatherD);
        // torso: massive barrel chest and belly, short leather loincloth
        c.Shaded(15, 19, 35, 25, skin);
        c.Shaded(21, 31, 23, 12, skinL);
        c.Shaded(19, 42, 27, 8, leather);
        c.Rect(15, 40, 35, 4, leatherD);
        if (front)
        {
            c.Rect(29, 40, 7, 4, team_); c.Rect(30, 41, 5, 2, Lit(team_, 1.4f));                     // team-coloured belt plate
            c.Rect(31, 21, 3, 11, skinD); c.Rect(23, 26, 8, 2, skinD); c.Rect(34, 26, 8, 2, skinD);  // sternum and rib lines
            c.Rect(25, 44, 2, 6, leatherD); c.Rect(38, 44, 2, 6, leatherD);                          // loincloth folds
        }
        else
        {
            c.Rect(31, 19, 3, 21, skinD);                                                            // spine
            for (int i = 0; i < 4; i++) c.Rect(28, 21 + i * 5, 9, 2, skinD);                         // ridge
            c.Rect(25, 44, 15, 6, leatherD);
        }
        // left arm hangs with a big fist
        c.Shaded(7, 23, 9, 19, skin); c.Shaded(6, 40, 11, 8, skinD);
        // right arm and the launcher
        if (pose == EnemyPose.Fire && front)
        {
            c.Shaded(49, 14, 8, 15, skin); c.Shaded(45, 8, 14, 10, iron); c.Rect(46, 5, 12, 5, ironD); c.Rect(48, 6, 8, 3, C(16, 16, 20));
            c.Disc(52, 5, 6, C(255, 200, 80)); c.Disc(52, 5, 3, C(255, 250, 230));                  // muzzle flash
        }
        else
        {
            c.Shaded(49, 23, 8, 17, skin);
            c.Shaded(46, 36, 14, 10, iron); c.Rect(48, 44, 10, 4, ironD); c.Rect(50, 45, 6, 3, C(16, 16, 20));
        }
        // pauldrons with spikes, in the team colour
        c.Shaded(9, 15, 15, 9, team_); c.Shaded(41, 15, 15, 9, team_);
        for (int i = 0; i < 3; i++) { c.Rect(10 + i * 5, 11, 3, 5, iron); c.Rect(42 + i * 5, 11, 3, 5, iron); }
        // head sunk between the shoulders
        c.Shaded(23, 6, 19, 16, skinL);
        if (front)
        {
            c.Rect(24, 7, 17, 4, skinD);                                                             // heavy brow
            c.Rect(27, 11, 4, 3, C(250, 220, 60)); c.Rect(34, 11, 4, 3, C(250, 220, 60));          // glowing eyes
            c.Rect(28, 12, 2, 1, C(120, 30, 10)); c.Rect(35, 12, 2, 1, C(120, 30, 10));
            c.Rect(26, 17, 13, 4, C(46, 20, 18));                                                    // wide mouth
            c.Rect(27, 15, 2, 4, bone); c.Rect(36, 15, 2, 4, bone);                                  // tusks
            c.Rect(30, 18, 5, 2, bone);
        }
        else
        {
            c.Shaded(23, 5, 19, 11, skinD); c.Rect(22, 11, 2, 5, skin); c.Rect(41, 11, 2, 5, skin); // bald back of the head, ears
            c.Rect(28, 8, 9, 2, leatherD);
        }
        c.Outline(C(14, 12, 12));
        return c;
    }
}
