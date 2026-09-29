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
        AddPickups(g);
        AddVisuals(g);
        for (int i = 0; i < bots; i++) g.AddBot();
        return g;
    }

    /// <summary>Visual dressing: ceiling girders, trim, pillar collars, glowing fixtures and the lights they cast.</summary>
    static void AddVisuals(GameWorld g)
    {
        var pillars = new[] { (-900f, -900f), (900f, -900f), (-900f, 900f), (900f, 900f), (-1000f, 0f), (0f, -1000f), (0f, 1000f) };
        var warm = new Vector3(1.9f, 0.95f, 0.38f);
        var cold = new Vector3(0.35f, 0.85f, 1.3f);
        var red = new Vector3(1.8f, 0.18f, 0.12f);
        var lampCol = new Vector3(255, 214, 150);

        void Decor(Vector3 min, Vector3 max, Surface m, Vector3 color = default) => g.Decor.Add(new DecorBox(new Aabb(min, max), m, color));
        void Fixture(Vector3 c, Vector3 half, Vector3 color) => Decor(c - half, c + half, Surface.Emissive, color);
        void Light(Vector3 p, Vector3 col, float radius, bool flicker = false) => g.Lights.Add(new MapLight { Position = p, Color = col, Radius = radius, Flicker = flicker });

        // ceiling girders: a grid of steel beams hugging the ceiling
        for (float z = -1536; z <= 1536; z += 1024) Decor(new(-Half, Height - 64, z - 48), new(Half, Height, z + 48), Surface.Metal);
        for (float x = -1536; x <= 1536; x += 1024) Decor(new(x - 48, Height - 96, -Half), new(x + 48, Height - 64, Half), Surface.Metal);

        // skirting along the outer walls
        Decor(new(-Half, 0, -Half), new(Half, 20, -Half + 24), Surface.Metal);
        Decor(new(-Half, 0, Half - 24), new(Half, 20, Half), Surface.Metal);
        Decor(new(-Half, 0, -Half), new(-Half + 24, 20, Half), Surface.Metal);
        Decor(new(Half - 24, 0, -Half), new(Half, 20, Half), Surface.Metal);

        // pillars: base and cap collars, plus a wall torch on the face toward the centre
        foreach (var (px, pz) in pillars)
        {
            Decor(new(px - 60, 0, pz - 60), new(px + 60, 36, pz + 60), Surface.Metal);
            Decor(new(px - 60, Height - 40, pz - 60), new(px + 60, Height, pz + 60), Surface.Metal);
            var toCentre = new Vector3(-px, 0, -pz);
            toCentre = toCentre.LengthSquared() < 1 ? Vector3.UnitZ : Vector3.Normalize(toCentre);
            var torch = new Vector3(px, 300, pz) + toCentre * 62f;
            Fixture(torch, new(9, 22, 9), new Vector3(255, 150, 60));
            Light(torch + toCentre * 30f, warm, 760f, flicker: true);
        }

        // bunkers: a cold lamp inside each, a warm one over each doorway
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            float cx = sx * 1500f, cz = sz * 1500f;
            Fixture(new(cx, 176, cz + 140), new(28, 6, 8), new Vector3(120, 210, 255));
            Light(new(cx, 150, cz + 140), cold, 760f);
            Light(new(cx - sx * 300f, 150, cz), warm * 0.8f, 520f, flicker: true);
            Fixture(new(cx - sx * 262f, 168, cz), new(6, 12, 10), new Vector3(255, 150, 60));
        }

        // mesa: glowing edge strips and red corner beacons
        Fixture(new(0, 132, -384), new(384, 4, 3), new Vector3(255, 90, 30));
        Fixture(new(0, 132, 384), new(384, 4, 3), new Vector3(255, 90, 30));
        Fixture(new(-384, 132, 0), new(3, 4, 384), new Vector3(255, 90, 30));
        Fixture(new(384, 132, 0), new(3, 4, 384), new Vector3(255, 90, 30));
        foreach (var (mx, mz) in new[] { (-340f, -340f), (340f, -340f), (-340f, 340f), (340f, 340f) })
        {
            Decor(new(mx - 14, 128, mz - 14), new(mx + 14, 178, mz + 14), Surface.Metal);
            Fixture(new(mx, 190, mz), new(12, 12, 12), new Vector3(255, 40, 30));
            Light(new(mx, 215, mz), red, 520f, flicker: true);
        }

        // east ledge
        foreach (float lz in new[] { -420f, 420f })
        {
            Fixture(new(2020, 250, lz), new(8, 24, 10), new Vector3(255, 150, 60));
            Light(new(1985, 250, lz), warm, 700f, flicker: true);
        }

        // perimeter wall lamps
        foreach (float t in new[] { -1200f, 0f, 1200f })
        {
            foreach (var (x, z, dx, dz) in new[] { (t, -Half + 30, 0, 1), (t, Half - 30, 0, -1), (-Half + 30, t, 1, 0), (Half - 30, t, -1, 0) })
            {
                Fixture(new(x, 230, z), new(dx == 0 ? 22 : 6, 30, dz == 0 ? 22 : 6), new Vector3(255, 160, 70));
                Light(new(x + dx * 40f, 230, z + dz * 40f), warm * 0.85f, 800f, flicker: t == 0);
            }
        }

        // ceiling lamps: soft general illumination over the open floor
        foreach (float lx in new[] { -1300f, 0f, 1300f })
            foreach (float lz in new[] { -1300f, 0f, 1300f })
            {
                Fixture(new(lx, Height - 100, lz), new(56, 4, 56), lampCol);
                Light(new(lx, Height - 130, lz), new Vector3(1.0f, 0.82f, 0.6f), 1500f);
            }
    }

    static void AddPickups(GameWorld g)
    {
        void Health(float x, float y, float z) => g.Pickups.Add(new Pickup { Kind = PickupKind.Health, Amount = 25, Position = new(x, y, z) });
        void Ammo(PickupKind k, int n, float x, float y, float z) => g.Pickups.Add(new Pickup { Kind = k, Amount = n, Position = new(x, y, z) });
        void Gun(WeaponId w, float x, float y, float z) => g.Pickups.Add(new Pickup { Kind = PickupKind.Weapon, Weapon = w, Position = new(x, y, z) });

        // Weapons: the double shotgun on the mesa, one gun in each of three bunkers, the rocket launcher on the east ledge.
        Gun(WeaponId.SuperShotgun, 0, 144, 0);
        Gun(WeaponId.Nailgun, -1600, 16, -1500);
        Gun(WeaponId.SuperNailgun, 1600, 16, -1500);
        Gun(WeaponId.GrenadeLauncher, -1600, 16, 1500);
        Gun(WeaponId.RocketLauncher, 1950, 144, 0);

        // Health (25 each)
        Health(200, 144, 200); Health(-200, 144, -200);
        Health(1600, 16, 1500);
        Health(0, 16, -1300); Health(0, 16, 1300);
        Health(-1300, 16, 300); Health(1300, 16, -300);

        // Ammo, sitting beside the pillar ring
        Ammo(PickupKind.Shells, 20, -800, 16, -900); Ammo(PickupKind.Shells, 20, 800, 16, 900);
        Ammo(PickupKind.Nails, 50, 800, 16, -900); Ammo(PickupKind.Nails, 50, -800, 16, 900);
        Ammo(PickupKind.Rockets, 5, 900, 16, -780); Ammo(PickupKind.Rockets, 5, -900, 16, 780);
        Ammo(PickupKind.Rockets, 5, 1950, 144, 300);
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
