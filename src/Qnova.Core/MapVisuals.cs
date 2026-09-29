using System.Numerics;

namespace Qnova.Core;

/// <summary>Surface types. The renderer maps each to a procedural texture and colour.</summary>
public enum Surface : byte { Floor = 0, Wall = 1, Metal = 2, Flat = 3, Ceiling = 4, Emissive = 5 }

/// <summary>A visual-only box (no collision): ceiling girders, trim, light fixtures.</summary>
public readonly record struct DecorBox(Aabb Box, Surface Surface, Vector3 Color = default);

/// <summary>A static point light. Colour is linear RGB and may exceed 1 for a brighter light; radius is in world units.</summary>
public sealed class MapLight
{
    public Vector3 Position;
    public Vector3 Color = Vector3.One;
    public float Radius = 600f;
    public bool Flicker;
}

public static class SurfaceRules
{
    /// <summary>Pick a material for a solid from its shape: slabs and steps read as stone, thin tall walls as
    /// concrete, low blocks and thin cover walls as metal, the top plate as ceiling.</summary>
    public static Surface For(Aabb b, float ceilingY)
    {
        var s = b.Max - b.Min;
        if (b.Max.Y <= 0.01f) return Surface.Floor;
        if (b.Min.Y >= ceilingY - 1f) return Surface.Ceiling;
        if (s.Y <= 64f && s.X <= 200f && s.Z <= 200f && b.Min.Y <= 0.01f) return Surface.Metal;   // crates, cover blocks
        if (s.Y >= 300f) return Surface.Wall;                                                         // pillars, outer walls
        if (MathF.Min(s.X, s.Z) <= 72f && s.Y >= 90f) return s.Y <= 100f ? Surface.Metal : Surface.Wall;
        return Surface.Floor;   // mesa, ledge, stairs
    }
}
