using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class MapVisualsTests
{
    static Aabb Box(float x0, float y0, float z0, float x1, float y1, float z1) => new(new(x0, y0, z0), new(x1, y1, z1));

    [Fact]
    public void Surface_rules_classify_typical_solids()
    {
        const float ceil = 768;
        Assert.Equal(Surface.Floor, SurfaceRules.For(Box(-1024, -64, -1024, 1024, 0, 1024), ceil));
        Assert.Equal(Surface.Ceiling, SurfaceRules.For(Box(-1024, 768, -1024, 1024, 832, 1024), ceil));
        Assert.Equal(Surface.Wall, SurfaceRules.For(Box(-1088, -64, -1024, -1024, 832, 1024), ceil));      // outer wall
        Assert.Equal(Surface.Wall, SurfaceRules.For(Box(-48, 0, -48, 48, 768, 48), ceil));                // pillar
        Assert.Equal(Surface.Wall, SurfaceRules.For(Box(0, 0, 0, 512, 192, 32), ceil));                    // bunker wall
        Assert.Equal(Surface.Metal, SurfaceRules.For(Box(-48, 0, -48, 48, 40, 48), ceil));                // crate
        Assert.Equal(Surface.Metal, SurfaceRules.For(Box(-160, 0, -16, 160, 96, 16), ceil));               // cover wall
        Assert.Equal(Surface.Floor, SurfaceRules.For(Box(-384, 0, -384, 384, 128, 384), ceil));            // mesa
        Assert.Equal(Surface.Floor, SurfaceRules.For(Box(400, 0, -128, 448, 32, 128), ceil));              // stair step
    }

    [Fact]
    public void Arena_has_lights_and_decor_inside_the_map()
    {
        var g = Arena.Build(bots: 0);
        Assert.True(g.Lights.Count >= 30, $"only {g.Lights.Count} lights");
        Assert.True(g.Decor.Count >= 40, $"only {g.Decor.Count} decor boxes");
        Assert.Contains(g.Lights, l => l.Flicker);
        // warm, cold and red lights all exist (torches, bunker lamps, mesa beacons)
        Assert.Contains(g.Lights, l => l.Color.X > 1.5f && l.Color.Y < 1.1f && l.Color.Z < 0.5f);
        Assert.Contains(g.Lights, l => l.Color.Z > 1.0f && l.Color.X < 0.5f);
        Assert.Contains(g.Lights, l => l.Color.X > 1.5f && l.Color.Y < 0.3f);
        foreach (var l in g.Lights)
        {
            Assert.True(l.Radius > 0);
            Assert.InRange(l.Position.X, -Arena.Half, Arena.Half);
            Assert.InRange(l.Position.Z, -Arena.Half, Arena.Half);
            Assert.InRange(l.Position.Y, 0, Arena.Height);
            Assert.True(g.Map.IsEmpty(l.Position, new Vector3(1, 1, 1)), $"light at {l.Position} is buried in a solid");
        }
        foreach (var d in g.Decor)
        {
            Assert.InRange(d.Box.Min.X, -Arena.Half - 1, Arena.Half + 1);
            Assert.InRange(d.Box.Max.X, -Arena.Half - 1, Arena.Half + 1);
            Assert.InRange(d.Box.Min.Y, -1, Arena.Height + 1);
            Assert.InRange(d.Box.Max.Y, 0, Arena.Height + 1);
            if (d.Surface == Surface.Emissive) Assert.True(d.Color.LengthSquared() > 0, "fixture has no colour");
        }
    }

    [Fact]
    public void Decor_is_visual_only_and_does_not_block_movement()
    {
        var g = Arena.Build(bots: 0);
        // The ceiling girders overlap open air but must never stop a player walking under or rocket-jumping through them.
        Assert.Contains(g.Decor, d => d.Box.Min.Y > 600);
        var girder = g.Decor.First(d => d.Box.Min.Y > 600).Box;
        Assert.True(g.Map.IsEmpty(girder.Center, new Vector3(4, 4, 4)));
    }
}
