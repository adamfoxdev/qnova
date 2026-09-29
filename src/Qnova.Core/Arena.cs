using System.Numerics;

namespace Qnova.Core;

public static class Arena
{
    public const float Half = 2048f;      // interior is 4096 x 4096
    public const float Height = 768f;

    /// <summary>"qnova dm1": a 4096-unit-square arena with a central mesa, four walled bunkers, a pillar ring,
    /// an east ledge reached by stairs, crates and cover walls. Bots spawn at the corners and edges.</summary>
    public static GameWorld Build(int seed = 1, int bots = 1)
    {
        var w = new World();

        // floor, ceiling, outer walls
        w.Add(new(-Half, -64, -Half), new(Half, 0, Half));
        w.Add(new(-Half, Height, -Half), new(Half, Height + 64, Half));
        w.Add(new(-Half - 64, -64, -Half), new(-Half, Height + 64, Half));
        w.Add(new(Half, -64, -Half), new(Half + 64, Height + 64, Half));
        w.Add(new(-Half, -64, -Half - 64), new(Half, Height + 64, -Half));
        w.Add(new(-Half, -64, Half), new(Half, Height + 64, Half + 64));

        // central mesa (128 high) with 8-step stairs (16 high, 48 deep) on all four sides
        w.Add(new(-384, 0, -384), new(384, 128, 384));
        for (int k = 1; k <= 8; k++)
        {
            float d0 = 384 + 48 * (8 - k), d1 = d0 + 48, h = 16 * k;
            w.Add(new(d0, 0, -128), new(d1, h, 128));       // +X side
            w.Add(new(-d1, 0, -128), new(-d0, h, 128));     // -X side
            w.Add(new(-128, 0, d0), new(128, h, d1));       // +Z side
            w.Add(new(-128, 0, -d1), new(128, h, -d0));     // -Z side
        }

        // ring of tall pillars
        foreach (var (x, z) in new[] { (-900f, -900f), (900f, -900f), (-900f, 900f), (900f, 900f), (-1000f, 0f), (0f, -1000f), (0f, 1000f) })
            w.Add(new(x - 48, 0, z - 48), new(x + 48, Height, z + 48));

        // four bunkers (open-topped, 192-high walls, 192-wide doorways facing the centre)
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            Bunker(w, sx * 1500f, sz * 1500f, sx, sz);

        // east ledge (128 high) with stairs from the west
        w.Add(new(1792, 0, -1024), new(2048, 128, 1024));
        for (int k = 1; k <= 8; k++)
            w.Add(new(1792 - 48 * (9 - k), 0, -128), new(1792 - 48 * (8 - k), 16 * k, 128));

        // jumpable crates (40 high) and taller cover walls
        foreach (var (x, z) in new[] { (-600f, 300f), (600f, -300f), (300f, 600f), (-300f, -600f), (-1300f, 100f), (100f, 1300f), (1200f, 700f) })
            w.Add(new(x - 48, 0, z - 48), new(x + 48, 40, z + 48));
        foreach (var (x, z, along) in new[] { (-700f, -1300f, true), (700f, 1300f, true), (-1300f, 700f, false), (1300f, -700f, false), (0f, -1500f, true), (0f, 1500f, true) })
            w.Add(along ? new Vector3(x - 160, 0, z - 16) : new Vector3(x - 16, 0, z - 160),
                  along ? new Vector3(x + 160, 96, z + 16) : new Vector3(x + 16, 96, z + 160));

        var spawn = new Vector3(-1900, 28, -1900);
        var g = new GameWorld(w, spawn, seed);
        g.SpawnPoints.AddRange(new[]
        {
            spawn, new(1900, 28, 1900), new(1900, 28, -1900), new(-1900, 28, 1900),
            new(0, 28, -1800), new(0, 28, 1800), new(-1800, 28, 0), new(1900, 156, 0),
            new(-1500, 28, -1380), new(1500, 28, 1380),
        });

        foreach (var pos in new[] { new Vector3(-600, 28, -900), new(600, 28, 900), new(0, 156, 0) })
            g.Targets.Add(new Target { Origin = pos });
        for (int i = 0; i < bots; i++) g.AddBot();
        return g;
    }

    /// <summary>A 512 x 512 walled compound centred at (cx, cz); doorways open toward the map centre (sx, sz = corner signs).</summary>
    static void Bunker(World w, float cx, float cz, int sx, int sz)
    {
        const float r = 256, t = 32, h = 192, door = 96;   // door = half width
        // walls on the outer sides are solid
        w.Add(new(cx - r, 0, cz + sz * r - t / 2 * 1), new(cx + r, h, cz + sz * r + t / 2 * 1));   // outer Z wall
        w.Add(new(cx + sx * r - t / 2, 0, cz - r), new(cx + sx * r + t / 2, h, cz + r));           // outer X wall
        // inner Z wall (toward centre) with a doorway in the middle
        float iz = cz - sz * r;
        w.Add(new(cx - r, 0, iz - t / 2), new(cx - door, h, iz + t / 2));
        w.Add(new(cx + door, 0, iz - t / 2), new(cx + r, h, iz + t / 2));
        // inner X wall with a doorway
        float ix = cx - sx * r;
        w.Add(new(ix - t / 2, 0, cz - r), new(ix + t / 2, h, cz - door));
        w.Add(new(ix - t / 2, 0, cz + door), new(ix + t / 2, h, cz + r));
        // interior block for cover
        w.Add(new(cx - 40, 0, cz - 40), new(cx + 40, 64, cz + 40));
    }
}
