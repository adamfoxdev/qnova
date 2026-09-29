using System.Numerics;

namespace Qnova.Core;

/// <summary>A raised platform reached by stairs. <see cref="Top"/> is the walkable surface, <see cref="Foot"/> the floor point where
/// the stairs begin (a player-centre position) and <see cref="Dir"/> the horizontal direction from the foot up the stairs.</summary>
public sealed record Platform(Aabb Top, Vector3 Foot, Vector3 Dir);

/// <summary>Everything that makes up a map, independent of any running game, so maps can be built, checked and swapped in.</summary>
public sealed class MapData
{
    public string Name = "";
    public int Seed;
    public float Half = 2048f;          // interior is 2*Half square
    public float Height = 768f;         // ceiling height
    public Vector3 PlayerSpawn;
    public Vector3? RedFlag, BlueFlag;   // authored capture-the-flag bases (otherwise derived from the spawn points)

    public readonly List<Aabb> Solids = new();
    public readonly List<DecorBox> Decor = new();
    public readonly List<MapLight> Lights = new();
    public readonly List<Pickup> Pickups = new();
    public readonly List<JumpPad> JumpPads = new();
    public readonly List<Vector3> Spawns = new();
    public readonly List<Vector3> Dummies = new();
    public readonly List<Platform> Platforms = new();

    public string Summary =>
        $"{Name}: {(int)(Half * 2)}x{(int)(Half * 2)}, {Platforms.Count} platform(s), {JumpPads.Count} jump pad(s), " +
        $"{Pickups.Count} pickups, {Spawns.Count} spawn points";

    /// <summary>Capture the contents of a built game world (used for the hand-made classic arena).</summary>
    public static MapData From(GameWorld g, string name, int seed, float half, float height)
    {
        var m = new MapData { Name = name, Seed = seed, Half = half, Height = height, PlayerSpawn = g.SpawnPoint };
        m.Solids.AddRange(g.Map.Solids);
        m.Decor.AddRange(g.Decor);
        m.Lights.AddRange(g.Lights);
        m.Pickups.AddRange(g.Pickups);
        m.JumpPads.AddRange(g.JumpPads);
        m.Spawns.AddRange(g.SpawnPoints);
        foreach (var t in g.Targets) m.Dummies.Add(t.Origin);
        return m;
    }
}
