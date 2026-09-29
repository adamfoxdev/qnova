using System.Numerics;

namespace Qnova.Core;

public static class Arena
{
    /// <summary>A small test arena: walled box, pillars, a staircase up to a platform, low cover, and dummies.</summary>
    public static GameWorld Build(int seed = 1)
    {
        var w = new World();
        // floor, ceiling, walls (arena interior is 2048 x 384 x 2048)
        w.Add(new(-1024, -64, -1024), new(1024, 0, 1024));
        w.Add(new(-1024, 384, -1024), new(1024, 448, 1024));
        w.Add(new(-1088, -64, -1024), new(-1024, 448, 1024));
        w.Add(new(1024, -64, -1024), new(1088, 448, 1024));
        w.Add(new(-1024, -64, -1088), new(1024, 448, -1024));
        w.Add(new(-1024, -64, 1024), new(1024, 448, 1088));

        foreach (var (x, z) in new[] { (-400f, -400f), (400f, -400f), (-400f, 400f), (400f, 400f) })
            w.Add(new(x - 48, 0, z - 48), new(x + 48, 384, z + 48));

        // 8 steps of 16 units (walkable: step height is 18) up to a 128 high platform
        for (int i = 1; i <= 8; i++)
            w.Add(new(256 + 64 * (i - 1), 0, -128), new(256 + 64 * i, 16 * i, 128));
        w.Add(new(768, 0, -128), new(1024, 128, 128));

        w.Add(new(-200, 0, -16), new(200, 64, 16));   // low cover

        var g = new GameWorld(w, new Vector3(-700, 28, 0), seed);
        foreach (var pos in new[] { new(0, 28, -600), new(-600, 28, 600), new(600, 28, 600), new(900, 156, 0), new Vector3(0, 28, 600) })
            g.Targets.Add(new Target { Origin = pos });
        return g;
    }
}
