using System.Numerics;

namespace Qnova.Core;

/// <summary>Axis-aligned box. Y is up. Units are Quake units (player is 32 wide, 56 tall).</summary>
public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    public static Aabb FromCenter(Vector3 c, Vector3 half) => new(c - half, c + half);
    public Vector3 Center => (Min + Max) * 0.5f;
    public bool Overlaps(Aabb o) =>
        Min.X < o.Max.X && Max.X > o.Min.X &&
        Min.Y < o.Max.Y && Max.Y > o.Min.Y &&
        Min.Z < o.Max.Z && Max.Z > o.Min.Z;
}

public struct Trace
{
    public float Fraction;      // 0..1, 1 = no hit
    public Vector3 EndPos;
    public Vector3 Normal;      // surface normal of the hit plane (zero if no hit)
    public bool Hit => Fraction < 1f;
}

/// <summary>Static world made of solid boxes, with swept-box tracing (Quake style clip hull).</summary>
public sealed class World
{
    public const float SurfaceEpsilon = 0.03f;
    readonly List<Aabb> _solids = new();
    public IReadOnlyList<Aabb> Solids => _solids;

    public void Add(Aabb box) => _solids.Add(box);
    public void Add(Vector3 min, Vector3 max) => _solids.Add(new Aabb(min, max));

    /// <summary>Sweep a box with the given half extents from start to end against the world.</summary>
    public Trace TraceBox(Vector3 start, Vector3 end, Vector3 half)
    {
        var t = new Trace { Fraction = 1f, EndPos = end };
        var d = end - start;
        float len = d.Length();
        if (len < 1e-6f) return t;

        foreach (var s in _solids)
        {
            if (RayVsBox(start, d, s.Min - half, s.Max + half, out float f, out Vector3 n) && f < t.Fraction)
            {
                t.Fraction = f;
                t.Normal = n;
            }
        }
        if (t.Hit)
        {
            // Back off from the surface so we never end up embedded in it.
            float f = MathF.Max(0f, t.Fraction - SurfaceEpsilon / len);
            t.Fraction = f;
            t.EndPos = start + d * f;
        }
        return t;
    }

    public Trace TraceRay(Vector3 start, Vector3 end) => TraceBox(start, end, Vector3.Zero);

    public bool IsEmpty(Vector3 pos, Vector3 half)
    {
        var b = Aabb.FromCenter(pos, half);
        foreach (var s in _solids) if (b.Overlaps(s)) return false;
        return true;
    }

    /// <summary>Slab test. Starting inside or exactly touching the box counts as a miss (lets you move out).</summary>
    public static bool RayVsBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float tHit, out Vector3 normal)
    {
        tHit = 0; normal = Vector3.Zero;
        float tEnter = float.NegativeInfinity, tExit = float.PositiveInfinity;
        int axis = -1; float sign = 0;
        for (int i = 0; i < 3; i++)
        {
            float oi = Get(o, i), di = Get(d, i), lo = Get(min, i), hi = Get(max, i);
            if (MathF.Abs(di) < 1e-9f)
            {
                if (oi <= lo || oi >= hi) return false;
                continue;
            }
            float t1 = (lo - oi) / di, t2 = (hi - oi) / di;
            float near = MathF.Min(t1, t2), far = MathF.Max(t1, t2);
            if (near > tEnter) { tEnter = near; axis = i; sign = di > 0 ? -1f : 1f; }
            if (far < tExit) tExit = far;
            if (tEnter > tExit) return false;
        }
        if (axis < 0 || tEnter < 0f || tEnter > 1f) return false;
        tHit = tEnter;
        normal = axis switch { 0 => new Vector3(sign, 0, 0), 1 => new Vector3(0, sign, 0), _ => new Vector3(0, 0, sign) };
        return true;
    }

    /// <summary>Ray vs box that reports distance along a unit direction (used for hitscan against entities).</summary>
    public static bool RayVsBox(Vector3 o, Vector3 d, Aabb box, out float t) =>
        RayVsBox(o, d, box.Min, box.Max, out t, out _);

    static float Get(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
}
