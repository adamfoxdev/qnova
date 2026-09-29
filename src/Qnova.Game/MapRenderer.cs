using System.Numerics;
using Qnova.Core;
using Raylib_cs;

/// <summary>One light as fed to the shader (world units).</summary>
readonly record struct LightSrc(Vector3 Pos, Vector3 Color, float Radius);

/// <summary>Draws the arena with a lighting + procedural-texture shader: stone slabs, concrete blocks, riveted metal,
/// grime, point lights and dark distance fog. Cubes carry their material id in the vertex alpha.</summary>
sealed class MapRenderer
{
    const float S = 1f / 32f;      // world units -> render units
    public const int MaxLights = 16;

    readonly Shader _shader;
    readonly int _locView, _locLightPos, _locLightCol, _locTime;
    readonly Vector4[] _pos = new Vector4[MaxLights];
    readonly Vector3[] _col = new Vector3[MaxLights];

    const string Vert = @"#version 330
in vec3 vertexPosition;
in vec3 vertexNormal;
in vec4 vertexColor;
uniform mat4 mvp;
out vec3 fragPos;
out vec3 fragNormal;
out vec4 fragColor;
void main()
{
    fragPos = vertexPosition;     // immediate-mode cubes are already transformed to world space on the CPU
    fragNormal = vertexNormal;
    fragColor = vertexColor;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}";

    const string Frag = @"#version 330
in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;
out vec4 finalColor;
uniform vec3 viewPos;
uniform vec4 lightPos[16];   // xyz = position, w = radius
uniform vec3 lightCol[16];
uniform float uTime;
const vec3 fogCol = vec3(0.030, 0.023, 0.025);

float hash(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float vnoise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * vnoise(p); p *= 2.03; a *= 0.5; }
    return v;
}

// q is the surface position in world units on the plane facing the viewer
vec3 surface(int mat, vec3 base, vec2 q, vec3 n)
{
    if (mat == 0)   // floor: 128-unit stone slabs
    {
        vec2 c = floor(q / 128.0), f = fract(q / 128.0);
        float edge = min(min(f.x, f.y), min(1.0 - f.x, 1.0 - f.y)) * 128.0;
        float grout = smoothstep(0.0, 3.0, edge);
        float g = fbm(q * 0.045 + hash(c) * 9.0);
        vec3 a = base * (0.72 + 0.4 * hash(c)) * (0.42 + 1.0 * g);
        a += vec3(0.05) * (smoothstep(3.0, 6.0, edge) - smoothstep(6.0, 10.0, edge));   // chamfer highlight
        float crack = smoothstep(0.012, 0.0, abs(fbm(q * 0.018 + c * 3.1) - 0.5));
        a *= 1.0 - 0.5 * crack;
        return a * mix(0.28, 1.0, grout);
    }
    if (mat == 1)   // wall: concrete blocks, 128 x 64, running bond
    {
        float row = floor(q.y / 64.0);
        float xo = q.x + 64.0 * mod(row, 2.0);
        vec2 c = vec2(floor(xo / 128.0), row);
        vec2 f = vec2(fract(xo / 128.0), fract(q.y / 64.0));
        float edge = min(min(f.x * 128.0, (1.0 - f.x) * 128.0), min(f.y * 64.0, (1.0 - f.y) * 64.0));
        float grout = smoothstep(0.0, 2.5, edge);
        float stain = fbm(vec2(q.x * 0.03, q.y * 0.006) + c.x);
        vec3 a = base * (0.7 + 0.5 * hash(c)) * (0.45 + 0.9 * fbm(q * 0.05)) * (0.62 + 0.6 * stain);
        a = mix(a, a * vec3(1.25, 0.9, 0.7), smoothstep(0.55, 0.85, fbm(q * 0.02 + 7.0)) * 0.6);   // rust bloom
        return a * mix(0.3, 1.0, grout);
    }
    if (mat == 2)   // metal: bolted 64-unit plates
    {
        vec2 f = fract(q / 64.0);
        float edge = min(min(f.x * 64.0, (1.0 - f.x) * 64.0), min(f.y * 64.0, (1.0 - f.y) * 64.0));
        float seam = smoothstep(0.0, 2.0, edge);
        float bevel = smoothstep(2.0, 3.5, edge) - smoothstep(3.5, 6.0, edge);
        vec2 corner = min(f, 1.0 - f) * 64.0;
        float rivet = 1.0 - smoothstep(2.0, 3.2, length(corner - vec2(8.0)));
        float scratch = vnoise(vec2(q.x * 0.6, q.y * 0.03)) * 0.5 + vnoise(vec2(q.x * 0.03, q.y * 0.5)) * 0.5;
        vec3 a = base * (0.55 + 0.7 * scratch) * (0.6 + 0.7 * fbm(q * 0.04));
        a += vec3(0.07) * bevel + vec3(0.12) * rivet;
        return a * mix(0.3, 1.0, seam);
    }
    if (mat == 4)   // ceiling: dark plating
    {
        vec2 f = fract(q / 256.0);
        float edge = min(min(f.x, f.y), min(1.0 - f.x, 1.0 - f.y)) * 256.0;
        return base * (0.5 + 0.7 * fbm(q * 0.03)) * mix(0.35, 1.0, smoothstep(0.0, 3.0, edge));
    }
    return base;    // flat
}

void main()
{
    vec3 n = normalize(fragNormal);
    int mat = int(fragColor.a * 255.0 + 0.5);
    vec3 base = fragColor.rgb;

    // world units on the plane of the face (x,z for floors/ceilings; horizontal + height for walls)
    vec2 q = (abs(n.y) > 0.5) ? fragPos.xz : (abs(n.x) > 0.5 ? fragPos.zy : fragPos.xy);
    q *= 32.0;

    float fd = length(viewPos - fragPos);
    if (mat == 5)   // emissive fixtures ignore lighting
    {
        finalColor = vec4(mix(base * 1.5, fogCol, 1.0 - exp(-fd * 0.006)), 1.0);
        return;
    }

    vec3 albedo = surface(mat, base, q, n);
    float hUnits = fragPos.y * 32.0;
    float ao = (abs(n.y) < 0.5) ? mix(0.55, 1.0, smoothstep(0.0, 90.0, hUnits)) : 1.0;   // grime pooled at the base of walls
    ao *= (abs(n.y) < 0.5) ? mix(0.75, 1.0, smoothstep(760.0, 640.0, hUnits)) : 1.0;     // and darkening under the ceiling
    vec3 lit = albedo * vec3(0.27, 0.22, 0.23) * (0.6 + 0.4 * (n.y * 0.5 + 0.5)) * ao;

    for (int i = 0; i < 16; i++)
    {
        float rad = lightPos[i].w;
        if (rad <= 0.0) continue;
        vec3 L = lightPos[i].xyz - fragPos;
        float d = length(L);
        if (d >= rad) continue;
        float att = 1.0 - d / rad;
        att *= att;
        float ndl = max(dot(n, L / max(d, 0.001)), 0.0);
        lit += albedo * lightCol[i] * att * (0.22 + 1.05 * ndl) * ao * 1.35;
    }

    lit = lit / (1.0 + 0.22 * lit);                 // soft shoulder so hot lights don't clip
    float fog = 1.0 - exp(-fd * 0.0075);
    finalColor = vec4(mix(lit, fogCol, clamp(fog, 0.0, 1.0)), 1.0);
}";

    public MapRenderer()
    {
        _shader = Raylib.LoadShaderFromMemory(Vert, Frag);
        _locView = Raylib.GetShaderLocation(_shader, "viewPos");
        _locLightPos = Raylib.GetShaderLocation(_shader, "lightPos");
        _locLightCol = Raylib.GetShaderLocation(_shader, "lightCol");
        _locTime = Raylib.GetShaderLocation(_shader, "uTime");
    }

    public void Unload() => Raylib.UnloadShader(_shader);

    /// <summary>Pick the most influential lights for this view and upload the uniforms.</summary>
    public void Frame(Vector3 camera, IEnumerable<LightSrc> lights, float time)
    {
        var chosen = lights.OrderBy(l => Vector3.Distance(l.Pos, camera) - l.Radius).Take(MaxLights).ToList();
        for (int i = 0; i < MaxLights; i++)
        {
            if (i < chosen.Count) { _pos[i] = new Vector4(chosen[i].Pos * S, chosen[i].Radius * S); _col[i] = chosen[i].Color; }
            else { _pos[i] = default; _col[i] = default; }
        }
        var view = camera * S;
        Raylib.SetShaderValue(_shader, _locView, view, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValueV(_shader, _locLightPos, _pos, ShaderUniformDataType.Vec4, MaxLights);
        Raylib.SetShaderValueV(_shader, _locLightCol, _col, ShaderUniformDataType.Vec3, MaxLights);
        Raylib.SetShaderValue(_shader, _locTime, time, ShaderUniformDataType.Float);
    }

    public void Begin() => Raylib.BeginShaderMode(_shader);
    public void End() => Raylib.EndShaderMode();

    /// <summary>Draw a box given in world units; the colour's alpha carries the material id.</summary>
    public static void Box(Aabb b, Surface m, Color rgb)
    {
        var c = b.Center * S; var size = (b.Max - b.Min) * S;
        Raylib.DrawCubeV(c, size, new Color(rgb.R, rgb.G, rgb.B, (byte)m));
    }

    public static Color Palette(Surface m, Aabb b)
    {
        // stable per-solid variation so neighbouring blocks don't look identical
        float v = 0.9f + 0.2f * ((MathF.Abs(MathF.Sin(b.Center.X * 0.013f + b.Center.Z * 0.007f + b.Center.Y * 0.031f)) * 977f) % 1f);
        Color Scale(int r, int g, int bl) => new((byte)Math.Clamp((int)(r * v), 0, 255), (byte)Math.Clamp((int)(g * v), 0, 255), (byte)Math.Clamp((int)(bl * v), 0, 255), (byte)255);
        return m switch
        {
            Surface.Floor => Scale(126, 112, 100),
            Surface.Wall => Scale(112, 98, 90),
            Surface.Metal => Scale(100, 108, 120),
            Surface.Ceiling => Scale(62, 58, 62),
            _ => Scale(160, 160, 160),
        };
    }
}
