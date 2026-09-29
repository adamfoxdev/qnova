using System.Numerics;
using Qnova.Core;
using Raylib_cs;

/// <summary>Procedurally painted pixel-art sprites for weapons and items (no image assets).
/// Each is drawn on a small RGBA canvas, given a dark outline, and uploaded as a point-filtered texture.</summary>
sealed class ItemSprites : IDisposable
{
    public const int Size = 64;
    readonly Dictionary<SpriteId, Texture2D> _tex = new();
    Texture2D _glow;

    public ItemSprites()
    {
        foreach (SpriteId id in Enum.GetValues<SpriteId>()) _tex[id] = Upload(Paint(id));
        _glow = Upload(PaintGlow());
    }

    public Texture2D Get(SpriteId id) => _tex[id];
    public Texture2D Glow => _glow;

    public void Dispose()
    {
        foreach (var t in _tex.Values) Raylib.UnloadTexture(t);
        Raylib.UnloadTexture(_glow);
    }

    internal static Texture2D Upload(Canvas c)
    {
        var img = Raylib.GenImageColor(Size, Size, new Color(0, 0, 0, 0));
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var p = c.Px[y * Size + x];
                if (p.A != 0) Raylib.ImageDrawPixel(ref img, x, y, p);
            }
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(tex, TextureFilter.Point);
        return tex;
    }

    internal sealed class Canvas
    {
        public readonly Color[] Px = new Color[Size * Size];
        public void Set(int x, int y, Color c) { if ((uint)x < Size && (uint)y < Size) Px[y * Size + x] = c; }
        public void Rect(int x, int y, int w, int h, Color c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Set(i, j, c); }
        /// <summary>Rect with a light top edge and dark bottom edge, for cheap volume.</summary>
        public void Shaded(int x, int y, int w, int h, Color c)
        {
            Rect(x, y, w, h, c);
            if (h >= 3) { Rect(x, y, w, 1, Lit(c, 1.35f)); Rect(x, y + h - 1, w, 1, Lit(c, 0.6f)); }
        }
        public void Disc(int cx, int cy, int r, Color c)
        {
            for (int j = -r; j <= r; j++) for (int i = -r; i <= r; i++) if (i * i + j * j <= r * r + r / 2) Set(cx + i, cy + j, c);
        }
        public void Line(int x0, int y0, int x1, int y1, int th, Color c)
        {
            int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)); if (n == 0) n = 1;
            for (int s = 0; s <= n; s++)
            {
                int x = x0 + (x1 - x0) * s / n, y = y0 + (y1 - y0) * s / n;
                Rect(x - th / 2, y - th / 2, th, th, c);
            }
        }
        public void Outline(Color c)
        {
            var src = (Color[])Px.Clone();
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (src[y * Size + x].A != 0) continue;
                    bool edge = false;
                    for (int j = -1; j <= 1 && !edge; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int nx = x + i, ny = y + j;
                            if ((uint)nx < Size && (uint)ny < Size && src[ny * Size + nx].A != 0) { edge = true; break; }
                        }
                    if (edge) Px[y * Size + x] = c;
                }
        }
    }

    internal static Color C(int r, int g, int b) => new(r, g, b, 255);
    internal static Color Lit(Color c, float f) => new((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255), (byte)255);

    static readonly Color Steel = C(120, 124, 136), DarkSteel = C(70, 72, 82), Wood = C(126, 84, 44), DarkWood = C(84, 54, 30),
        Brass = C(214, 172, 60), Olive = C(96, 110, 58), Red = C(200, 50, 44), Blue = C(70, 140, 255), Cyan = C(90, 240, 220), White = C(236, 236, 240);

    static Canvas PaintGlow()
    {
        var c = new Canvas();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = MathF.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) / 32f;
                float a = Math.Clamp(1f - d, 0f, 1f);
                c.Px[y * Size + x] = new Color((byte)255, (byte)255, (byte)255, (byte)(a * a * 200));
            }
        return c;
    }

    static Canvas Paint(SpriteId id)
    {
        var c = new Canvas();
        switch (id)
        {
            case SpriteId.Health:   // white first-aid case with a red cross
                c.Shaded(10, 16, 44, 34, White);
                c.Shaded(24, 10, 16, 8, DarkSteel);                     // handle
                c.Rect(29, 21, 6, 24, Red); c.Rect(20, 30, 24, 6, Red);
                break;
            case SpriteId.Shells:   // two shotgun shells standing up
                for (int i = 0; i < 2; i++)
                {
                    int x = 14 + i * 22;
                    c.Shaded(x, 14, 16, 28, Red);
                    c.Shaded(x, 42, 16, 9, Brass);
                    c.Rect(x + 3, 46, 10, 2, Lit(Brass, 0.7f));
                }
                break;
            case SpriteId.Nails:    // box of nails, heads showing
                c.Shaded(8, 30, 48, 24, C(120, 100, 76));
                c.Rect(8, 36, 48, 3, C(90, 74, 56));
                for (int i = 0; i < 6; i++) { c.Rect(12 + i * 8, 12 + (i % 2) * 4, 2, 22, Steel); c.Rect(10 + i * 8, 10 + (i % 2) * 4, 6, 3, Lit(Steel, 1.3f)); }
                break;
            case SpriteId.Rockets:  // one big rocket, angled
                c.Line(14, 50, 46, 18, 9, Red);
                c.Line(14, 50, 46, 18, 3, Lit(Red, 1.4f));
                c.Disc(50, 14, 6, DarkSteel);
                c.Line(8, 56, 20, 44, 5, Brass); c.Rect(6, 40, 10, 5, DarkSteel); c.Rect(18, 54, 5, 8, DarkSteel);
                break;
            case SpriteId.Cells:    // blue energy cell
                c.Shaded(18, 12, 28, 42, C(50, 60, 90));
                c.Rect(24, 18, 16, 30, Blue); c.Rect(26, 20, 4, 26, Lit(Blue, 1.5f));
                c.Shaded(24, 6, 16, 8, Steel);
                c.Line(28, 40, 36, 30, 2, White); c.Line(36, 30, 30, 30, 2, White); c.Line(30, 30, 38, 20, 2, White);
                break;
            case SpriteId.Slugs:    // heavy slugs
                for (int i = 0; i < 3; i++)
                {
                    int x = 8 + i * 17;
                    c.Shaded(x, 18, 13, 30, Cyan);
                    c.Disc(x + 6, 18, 6, Lit(Cyan, 1.3f));
                    c.Shaded(x, 46, 13, 8, DarkSteel);
                }
                break;
            case SpriteId.Shotgun:
                c.Shaded(20, 26, 40, 4, Steel); c.Shaded(20, 30, 36, 4, DarkSteel);     // barrel + magazine tube
                c.Shaded(30, 34, 12, 5, Wood);                                          // pump
                c.Shaded(6, 27, 18, 9, DarkWood); c.Line(6, 30, 0, 44, 5, Wood);        // receiver + stock
                break;
            case SpriteId.SuperShotgun:
                c.Shaded(18, 24, 44, 4, Steel); c.Shaded(18, 28, 44, 4, Lit(Steel, 0.85f));    // double barrel
                c.Shaded(12, 26, 14, 10, DarkSteel);
                c.Shaded(26, 33, 14, 4, Wood);
                c.Line(12, 30, 0, 46, 6, Wood); c.Rect(14, 36, 3, 7, DarkWood);
                break;
            case SpriteId.Nailgun:
                c.Shaded(6, 22, 34, 14, Steel);
                c.Shaded(38, 26, 24, 5, DarkSteel);                                     // barrel
                c.Shaded(14, 14, 20, 8, Brass);                                         // nail magazine
                c.Shaded(12, 36, 8, 14, DarkWood);                                      // grip
                c.Rect(44, 31, 12, 3, Steel);
                break;
            case SpriteId.SuperNailgun:
                c.Shaded(4, 20, 30, 18, Steel);
                c.Disc(24, 20, 9, Brass); c.Disc(24, 20, 4, DarkSteel);                 // drum
                for (int i = 0; i < 3; i++) c.Shaded(34, 22 + i * 5, 28, 4, i == 1 ? Steel : DarkSteel);   // barrel cluster
                c.Shaded(10, 38, 8, 14, DarkWood);
                break;
            case SpriteId.GrenadeLauncher:
                c.Disc(30, 28, 12, C(96, 100, 84)); c.Disc(30, 28, 7, DarkSteel);        // cylinder
                c.Shaded(34, 24, 28, 9, Olive);                                          // barrel
                c.Shaded(8, 26, 14, 8, DarkWood); c.Rect(24, 40, 6, 12, DarkWood);
                break;
            case SpriteId.RocketLauncher:
                c.Shaded(6, 20, 54, 16, Olive);
                c.Shaded(52, 17, 10, 22, DarkSteel); c.Rect(55, 22, 4, 12, C(24, 24, 28));   // muzzle
                c.Shaded(8, 17, 8, 22, DarkSteel);
                c.Rect(28, 36, 6, 12, DarkWood); c.Rect(20, 14, 18, 5, Steel);          // grip + sight
                c.Rect(28, 26, 12, 3, Red);                                              // warning stripe
                break;
            case SpriteId.LightningGun:
                c.Shaded(6, 24, 30, 14, DarkSteel);
                c.Shaded(34, 27, 18, 8, Steel);
                c.Line(52, 24, 62, 20, 3, Steel); c.Line(52, 38, 62, 42, 3, Steel);      // prongs
                c.Line(62, 22, 62, 40, 2, Cyan); c.Line(58, 26, 62, 30, 2, White);        // arc
                c.Disc(20, 31, 6, Blue); c.Disc(20, 31, 3, White);                       // coil
                c.Rect(10, 38, 8, 12, DarkWood);
                break;
            case SpriteId.Railgun:
                c.Shaded(4, 24, 24, 12, DarkSteel);
                c.Shaded(26, 27, 36, 6, Steel);                                          // long barrel
                for (int i = 0; i < 3; i++) c.Rect(32 + i * 9, 25, 3, 10, Cyan);          // energy rings
                c.Rect(10, 28, 12, 4, Cyan);
                c.Rect(8, 36, 8, 14, DarkWood);
                break;
            case SpriteId.Axe:
                c.Line(10, 54, 40, 14, 5, Wood);
                c.Shaded(34, 6, 22, 20, Steel); c.Rect(52, 8, 5, 16, White);
                break;
        }
        c.Outline(C(18, 18, 22));
        return c;
    }
}
