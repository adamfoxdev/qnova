using System.Numerics;
using Qnova.Core;
using Raylib_cs;

/// <summary>Splash / menu screen: QNOVA in riveted steel with a live explosion inside the O, on a dark grimy backdrop.
/// Everything is drawn procedurally (no image assets). Particle kinds: fire blobs, sparks, smoke, embers.</summary>
sealed class Splash
{
    enum Kind : byte { Fire, Spark, Smoke, Ember }

    struct P
    {
        public Vector2 Pos, Vel;
        public float Life, Max, Size, Drag, Grav, Grow;
        public Kind Kind;
    }

    readonly List<P> _ps = new(600);
    readonly Random _rng = new(1996);
    readonly List<(Vector2 A, Vector2 B)> _cracks = new();      // unit coords over the title box
    readonly List<(Rectangle Rect, int Index)> _itemRects = new();   // only the rows currently on screen
    readonly Font _menuFont, _titleFont;
    readonly bool _menuCustom, _titleCustom;
    Texture2D _perlin, _cells;

    float _time, _nextBurst = 0.7f, _flash, _shake, _glow = 0.4f, _shock = -1f, _bloom = -1f;
    int _bursts;

    /// <summary>Raised on every explosion (first flag = the very first one after launch) so the caller can play a sound.</summary>
    public event Action<bool>? Exploded;
    public IReadOnlyList<(Rectangle Rect, int Index)> ItemRects => _itemRects;

    // Letter geometry: unit polylines (x,y in 0..1, y down). O is a ring, drawn separately.
    static readonly (char Ch, float W)[] Letters = { ('Q', 0.68f), ('N', 0.68f), ('O', 1.00f), ('V', 0.74f), ('A', 0.74f) };
    static readonly Dictionary<char, Vector2[][]> Strokes = new()
    {
        ['Q'] = new[]
        {
            new Vector2[] { new(0.30f, 0), new(0.70f, 0), new(1, 0.30f), new(1, 0.70f), new(0.70f, 1), new(0.30f, 1), new(0, 0.70f), new(0, 0.30f), new(0.30f, 0) },
            new Vector2[] { new(0.58f, 0.66f), new(1.06f, 1.10f) },
        },
        ['N'] = new[] { new Vector2[] { new(0, 1), new(0, 0), new(1, 1), new(1, 0) } },
        ['V'] = new[] { new Vector2[] { new(0, 0), new(0.5f, 1), new(1, 0) } },
        ['A'] = new[]
        {
            new Vector2[] { new(0, 1), new(0.5f, 0), new(1, 1) },
            new Vector2[] { new(0.20f, 0.70f), new(0.80f, 0.70f) },
        },
    };

    public Splash()
    {
        _menuFont = Fonts.Load(36, bold: true, out _menuCustom);
        _titleFont = _menuFont; _titleCustom = false;   // shares the menu font; only the menu owns/unloads it

        var img = Raylib.GenImagePerlinNoise(512, 512, 0, 0, 7f);
        _perlin = Raylib.LoadTextureFromImage(img); Raylib.UnloadImage(img);
        img = Raylib.GenImageCellular(256, 256, 18);
        _cells = Raylib.LoadTextureFromImage(img); Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_perlin, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(_cells, TextureFilter.Bilinear);

        for (int i = 0; i < 9; i++)   // fractures across the lettering
        {
            var p = new Vector2(R(0.03f, 0.97f), R(0.05f, 0.95f));
            for (int s = 0; s < 4; s++)
            {
                var q = p + new Vector2(R(-0.06f, 0.06f), R(0.02f, 0.10f) * (R(0, 1) < 0.5f ? -1 : 1));
                _cracks.Add((p, q)); p = q;
            }
        }
    }

    public void Unload()
    {
        Raylib.UnloadTexture(_perlin); Raylib.UnloadTexture(_cells);
        if (_menuCustom) Raylib.UnloadFont(_menuFont);
    }

    float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    // ---- geometry ----

    struct Layout { public float H, T, X0, Y0, SP; public Vector2 OCenter; public float ORing, OHole; public float[] LX; }

    static Layout MakeLayout(int w, int h)
    {
        var L = new Layout();
        L.H = MathF.Min(h * 0.27f, w * 0.15f);
        L.T = L.H * 0.17f;
        L.SP = L.H * 0.15f;
        float total = Letters.Sum(l => l.W) * L.H + L.SP * (Letters.Length - 1);
        L.X0 = (w - total) / 2f; L.Y0 = h * 0.09f;
        L.LX = new float[Letters.Length];
        float x = L.X0;
        for (int i = 0; i < Letters.Length; i++)
        {
            L.LX[i] = x;
            if (Letters[i].Ch == 'O') L.OCenter = new Vector2(x + L.H / 2f, L.Y0 + L.H / 2f);
            x += Letters[i].W * L.H + L.SP;
        }
        L.ORing = L.H / 2f;
        L.OHole = L.ORing - L.T * 1.05f;
        return L;
    }

    // ---- simulation ----

    public void Update(float dt, int w, int h)
    {
        dt = MathF.Min(dt, 0.05f);
        _time += dt;
        var L = MakeLayout(w, h);
        float sc = L.H / 200f;

        _nextBurst -= dt;
        if (_nextBurst <= 0) { Explode(L); _nextBurst = R(4.2f, 5.6f); }

        _flash = MathF.Max(0, _flash - dt * 3.2f);
        _shake = MathF.Max(0, _shake - dt * 2.4f);
        _glow += (0.40f - _glow) * MathF.Min(1, dt * 1.6f);
        if (_shock >= 0) { _shock += dt; if (_shock > 0.8f) _shock = -1; }
        if (_bloom >= 0) { _bloom += dt; if (_bloom > 1.2f) _bloom = -1; }

        // smouldering fire trapped in the ring, and embers drifting up from it
        Emit(dt, 46f, () => Fire(L.OCenter + Disc(L.OHole * 0.8f), new Vector2(R(-14, 14), -R(45, 120)) * sc, R(0.35f, 0.75f), L.H * R(0.05f, 0.11f), 0.9f, 0));
        Emit(dt, 14f, () => Ember(L.OCenter + Disc(L.OHole * 0.9f), new Vector2(R(-40, 40), -R(70, 200)) * sc, R(1.2f, 2.6f)));
        Emit(dt, 9f, () => Ember(new Vector2(R(0, w), h + 5), new Vector2(R(-20, 20), -R(30, 90)), R(3f, 6f)));

        for (int i = _ps.Count - 1; i >= 0; i--)
        {
            var p = _ps[i];
            p.Life += dt;
            if (p.Life >= p.Max) { _ps[i] = _ps[^1]; _ps.RemoveAt(_ps.Count - 1); continue; }
            p.Vel *= MathF.Max(0f, 1f - p.Drag * dt);
            p.Vel.Y += p.Grav * dt;
            if (p.Kind == Kind.Ember) p.Vel.X += MathF.Sin(_time * 3f + p.Pos.Y * 0.03f) * 30f * dt;
            p.Pos += p.Vel * dt;
            p.Size += p.Grow * dt;
            _ps[i] = p;
        }
    }

    float _acc0, _acc1, _acc2;
    void Emit(float dt, float perSecond, Action spawn)
    {
        ref float acc = ref (perSecond == 46f ? ref _acc0 : ref (perSecond == 14f ? ref _acc1 : ref _acc2));
        acc += dt * perSecond;
        while (acc >= 1f) { acc -= 1f; spawn(); }
    }

    Vector2 Disc(float r)
    {
        float a = R(0, MathF.Tau), d = MathF.Sqrt(R(0, 1)) * r;
        return new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
    }

    void Fire(Vector2 pos, Vector2 vel, float life, float size, float drag, float grow) =>
        _ps.Add(new P { Kind = Kind.Fire, Pos = pos, Vel = vel, Max = life, Size = size, Drag = drag, Grow = grow });
    void Ember(Vector2 pos, Vector2 vel, float life) =>
        _ps.Add(new P { Kind = Kind.Ember, Pos = pos, Vel = vel, Max = life, Size = R(1.2f, 2.8f), Drag = 0.2f });

    void Explode(Layout L)
    {
        float sc = L.H / 200f; var c = L.OCenter;
        for (int i = 0; i < 70; i++)   // fireball
        {
            float a = R(0, MathF.Tau), sp = R(0.15f, 1f) * 330f * sc;
            Fire(c + Disc(L.OHole * 0.5f), new Vector2(MathF.Cos(a), MathF.Sin(a)) * sp, R(0.55f, 1.3f), L.H * R(0.10f, 0.26f), 2.4f, L.H * 0.10f);
        }
        for (int i = 0; i < 180; i++)  // sparks
        {
            float a = R(0, MathF.Tau), sp = R(250f, 1200f) * sc;
            _ps.Add(new P { Kind = Kind.Spark, Pos = c + Disc(L.OHole * 0.4f), Vel = new Vector2(MathF.Cos(a), MathF.Sin(a) - 0.25f) * sp,
                            Max = R(0.5f, 1.7f), Size = R(1.6f, 3.4f), Drag = 0.7f, Grav = 520f * sc });
        }
        for (int i = 0; i < 36; i++)   // black smoke rolling upward
        {
            float a = R(0, MathF.Tau), sp = R(15f, 130f) * sc;
            _ps.Add(new P { Kind = Kind.Smoke, Pos = c + Disc(L.OHole), Vel = new Vector2(MathF.Cos(a) * sp, MathF.Sin(a) * sp - R(30f, 90f) * sc),
                            Max = R(2.4f, 4.2f), Size = L.H * R(0.14f, 0.28f), Drag = 0.6f, Grow = L.H * 0.10f });
        }
        _flash = 1f; _shake = 1f; _glow = 1f; _shock = 0f; _bloom = 0f;
        Exploded?.Invoke(_bursts++ == 0);
    }

    // ---- drawing ----

    static Color C(int r, int g, int b, int a = 255) => new Color((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(a, 0, 255));

    static Color Lerp(Color a, Color b, float t) => C(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t), (int)(a.A + (b.A - a.A) * t));

    static Color FireColor(float f)
    {
        if (f < 0.25f) return Lerp(C(255, 222, 150), C(255, 140, 35), f / 0.25f);
        if (f < 0.6f) return Lerp(C(255, 140, 35), C(225, 60, 12), (f - 0.25f) / 0.35f);
        return Lerp(C(225, 60, 12), C(110, 18, 8), (f - 0.6f) / 0.4f);
    }

    static void Glow(Vector2 c, float r, Color col, float alpha) =>
        Raylib.DrawCircleGradient((int)c.X, (int)c.Y, MathF.Max(1, r), C(col.R, col.G, col.B, (int)Math.Clamp(alpha * 255f, 0, 255)), C(col.R, col.G, col.B, 0));

    public void Draw(int w, int h, MenuModel menu, bool started)
    {
        var L = MakeLayout(w, h);
        float flick = 0.86f + 0.14f * MathF.Sin(_time * 23f) * MathF.Sin(_time * 7.3f + 1f);
        float heat = Math.Clamp(_glow * flick, 0f, 1.2f);

        DrawBackdrop(w, h, L, heat);

        var shake = new Vector2(R(-1, 1), R(-1, 1)) * (_shake * _shake * 14f);
        Raylib.BeginMode2D(new Camera2D { Offset = shake, Target = Vector2.Zero, Rotation = 0f, Zoom = 1f });
        DrawLetters(L);
        DrawCracks(L);

        // smoke (normal blend) drifts over the metal
        foreach (var p in _ps)
            if (p.Kind == Kind.Smoke)
            {
                float f = p.Life / p.Max;
                Glow(p.Pos, p.Size, C(26, 24, 22, 255), 0.55f * (1 - f) * MathF.Min(1f, f * 5f));
            }

        Raylib.BeginBlendMode(BlendMode.Additive);
        // the hole is a furnace
        Glow(L.OCenter, L.OHole * 1.05f, C(255, 150, 50, 255), 0.6f * heat);
        if (_bloom >= 0)   // the fireball swells out past the ring, then gutters
        {
            float bt = _bloom, r = L.H * (0.5f + 1.5f * MathF.Sqrt(MathF.Min(1f, bt / 0.4f)));
            float a = MathF.Pow(MathF.Max(0f, 1f - bt / 1.1f), 1.6f);
            Glow(L.OCenter, r, C(255, 150, 55, 255), 0.85f * a);
            Glow(L.OCenter, r * 0.5f, C(255, 225, 170, 255), 0.9f * a);
        }
        foreach (var p in _ps)
        {
            float f = p.Life / p.Max;
            switch (p.Kind)
            {
                case Kind.Fire:
                    var fc = FireColor(f);
                    Glow(p.Pos, p.Size * (0.7f + 0.5f * f), fc, 0.75f * MathF.Pow(1 - f, 1.15f));
                    break;
                case Kind.Spark:
                    var sc = FireColor(f * 0.9f);
                    var tail = p.Pos - p.Vel * 0.03f;
                    Raylib.DrawLineEx(p.Pos, tail, p.Size, C(sc.R, sc.G, sc.B, (int)(255 * (1 - f))));
                    break;
                case Kind.Ember:
                    float tw = 0.6f + 0.4f * MathF.Sin(p.Life * 18f + p.Pos.X);
                    Glow(p.Pos, p.Size * 3.2f, C(255, 140, 40, 255), 0.85f * (1 - f) * tw);
                    break;
            }
        }
        if (_shock >= 0)   // shockwave ring
        {
            float t = _shock / 0.8f, r = L.H * (0.35f + 1.9f * t);
            Raylib.DrawRing(L.OCenter, r - L.H * 0.05f * (1 - t), r, 0, 360, 72, C(255, 200, 120, (int)(150 * (1 - t))));
        }
        // hot inner rim + firelight spilling onto the neighbouring letters
        Raylib.DrawRing(L.OCenter, L.OHole - 2, L.OHole + L.T * 0.38f, 0, 360, 56, C(255, 130, 30, (int)Math.Clamp(150 * heat, 0, 255)));
        Glow(L.OCenter, L.H * 1.7f, C(255, 110, 25, 255), 0.30f * heat);
        Raylib.EndBlendMode();
        Raylib.EndMode2D();

        DrawMenu(w, h, L, menu, started, heat);
        DrawGrime(w, h);
        if (_flash > 0) Raylib.DrawRectangle(0, 0, w, h, C(255, 200, 140, (int)(70 * _flash * _flash)));
    }

    void DrawBackdrop(int w, int h, Layout L, float heat)
    {
        Raylib.DrawRectangle(0, 0, w, h, C(9, 7, 7, 255));
        Raylib.DrawTexturePro(_perlin, new Rectangle(0, 0, 512, 512), new Rectangle(0, 0, w, h), Vector2.Zero, 0, C(70, 52, 42, 105));
        Raylib.DrawTexturePro(_cells, new Rectangle(0, 0, 256, 256), new Rectangle(0, 0, w, h), Vector2.Zero, 0, C(60, 48, 42, 60));
        // warm bounce light behind the title, driven by the fire
        Raylib.BeginBlendMode(BlendMode.Additive);
        Glow(L.OCenter + new Vector2(0, L.H * 0.4f), L.H * 3.2f, C(200, 70, 15, 255), 0.30f * heat);
        Raylib.EndBlendMode();
    }

    void DrawLetters(Layout L)
    {
        float T = L.T;
        var shadow = C(0, 0, 0, 170);
        var outline = C(10, 7, 6, 255);
        var body = C(92, 86, 79, 255);
        var hi = C(158, 148, 132, 140);
        var rivet = C(60, 56, 52, 255);

        for (int i = 0; i < Letters.Length; i++)
        {
            var (ch, wf) = Letters[i];
            float x0 = L.LX[i], y0 = L.Y0, W = wf * L.H;
            if (ch == 'O')
            {
                var c = L.OCenter;
                Raylib.DrawRing(c + new Vector2(6, 9), L.OHole - 4, L.ORing + 4, 0, 360, 64, shadow);
                Raylib.DrawRing(c, L.OHole - 3, L.ORing + 3, 0, 360, 64, outline);
                Raylib.DrawRing(c, L.OHole, L.ORing, 0, 360, 64, body);
                Raylib.DrawRing(c + new Vector2(-2, -3), L.OHole + T * 0.55f, L.ORing - T * 0.12f, 190, 300, 24, hi);   // top-left sheen
                for (int k = 0; k < 8; k++)   // rivets around the ring
                {
                    float a = k * MathF.Tau / 8f + 0.2f, rr = (L.OHole + L.ORing) / 2f;
                    var rp = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
                    Raylib.DrawCircleV(rp, T * 0.11f, rivet);
                    Raylib.DrawCircleV(rp + new Vector2(-1, -1.5f), T * 0.045f, hi);
                }
                continue;
            }
            var polys = Strokes[ch].Select(poly => poly.Select(p => new Vector2(x0 + T * 0.5f + p.X * (W - T), y0 + T * 0.5f + p.Y * (L.H - T))).ToArray()).ToArray();
            foreach (var pts in polys) Stroke(pts, T + 8, shadow, new Vector2(6, 9));
            foreach (var pts in polys) Stroke(pts, T + 6, outline, Vector2.Zero);
            foreach (var pts in polys) Stroke(pts, T, body, Vector2.Zero);
            foreach (var pts in polys) Stroke(pts, T * 0.22f, hi, new Vector2(-T * 0.22f, -T * 0.22f));
            foreach (var pts in polys)
                foreach (var p in pts)
                {
                    Raylib.DrawCircleV(p, T * 0.11f, rivet);
                    Raylib.DrawCircleV(p + new Vector2(-1, -1.5f), T * 0.045f, hi);
                }
        }
    }

    static void Stroke(Vector2[] pts, float thick, Color col, Vector2 off)
    {
        for (int i = 0; i + 1 < pts.Length; i++) Raylib.DrawLineEx(pts[i] + off, pts[i + 1] + off, thick, col);
        foreach (var p in pts) Raylib.DrawCircleV(p + off, thick / 2f, col);   // round joins and caps
    }

    void DrawCracks(Layout L)
    {
        float tw = Letters.Sum(l => l.W) * L.H + L.SP * (Letters.Length - 1);
        foreach (var (a, b) in _cracks)
            Raylib.DrawLineEx(new Vector2(L.X0 + a.X * tw, L.Y0 + a.Y * L.H), new Vector2(L.X0 + b.X * tw, L.Y0 + b.Y * L.H), 1.6f, C(14, 10, 8, 200));
    }

    void DrawText(string text, float size, float x, float y, Color col, float spacing = 3f, bool shadow = true)
    {
        if (!_menuCustom) spacing = MathF.Max(spacing, 2f);
        if (shadow) Raylib.DrawTextEx(_menuFont, text, new Vector2(x + 2, y + 3), size, spacing, C(0, 0, 0, 230));
        Raylib.DrawTextEx(_menuFont, text, new Vector2(x, y), size, spacing, col);
    }

    Vector2 Measure(string text, float size, float spacing = 3f) => Raylib.MeasureTextEx(_menuFont, text, size, _menuCustom ? spacing : MathF.Max(spacing, 2f));

    void DrawCentered(string text, float size, float cx, float y, Color col, float spacing = 3f)
    {
        var m = Measure(text, size, spacing);
        DrawText(text, size, cx - m.X / 2f, y, col, spacing);
    }

    void DrawMenu(int w, int h, Layout L, MenuModel menu, bool started, float heat)
    {
        float cx = w / 2f;
        float size = _menuCustom ? MathF.Max(28, h * 0.048f) : 40;
        float small = _menuCustom ? MathF.Max(15, h * 0.024f) : 20;
        float top = L.Y0 + L.H;

        if (menu.AtRoot)
            DrawCentered("MOVE FAST.  SHOOT FIRST.  DON'T DIE.", small, cx, top + h * 0.045f, C(128, 84, 58, 255), 4f);
        else
            DrawCentered(menu.Current.Title, small * 1.35f, cx, top + h * 0.04f, C(190, 110, 60, 255), 8f);

        _itemRects.Clear();
        // Long lists (options, key bindings) squeeze the row pitch to fit, then scroll a window that follows the highlight.
        float y = top + h * (menu.AtRoot ? 0.14f : 0.105f), bottomLimit = h * 0.90f;
        int n = menu.Current.Items.Count;
        float minStep = size * 1.02f;
        int rows = Math.Clamp((int)((bottomLimit - y) / minStep), 3, n);
        float step = MathF.Max(minStep, MathF.Min(size * 1.55f, (bottomLimit - y) / rows));
        int first = Math.Clamp(menu.Selected - rows / 2, 0, n - rows);
        float colW = MathF.Min(w * 0.46f, 640f);
        if (first > 0) DrawCentered("^", small, cx, y - small * 1.5f, C(200, 110, 60, 255));
        if (first + rows < n) DrawCentered("v", small, cx, y + rows * step - small * 0.4f, C(200, 110, 60, 255));
        for (int i = first; i < first + rows; i++)
        {
            var it = menu.Current.Items[i];
            bool sel = i == menu.Selected;
            string label = it.Label();
            _itemRects.Add((new Rectangle(cx - colW / 2f - 40, y - 4, colW + 80, size + 10), i));

            if (sel)
            {
                float fl = 0.75f + 0.25f * MathF.Sin(_time * 9f + 1.3f) * MathF.Sin(_time * 3.1f);
                Raylib.BeginBlendMode(BlendMode.Additive);
                Raylib.DrawRectangleGradientH((int)(cx - colW / 2f - 60), (int)(y - 2), (int)(colW / 2f + 60), (int)size + 8, C(255, 90, 20, 0), C(255, 100, 25, (int)(70 * fl)));
                Raylib.DrawRectangleGradientH((int)cx, (int)(y - 2), (int)(colW / 2f + 60), (int)size + 8, C(255, 100, 25, (int)(70 * fl)), C(255, 90, 20, 0));
                Raylib.EndBlendMode();
            }
            var col = sel ? C(255, (int)(150 + 50 * MathF.Sin(_time * 9f)), 50, 255) : C(132, 122, 112, 255);

            if (it.Value == null)
            {
                DrawCentered(label, size, cx, y, col, 5f);
                if (sel)
                {
                    var m = Measure(label, size, 5f);
                    DrawText(">>", size, cx - m.X / 2f - size * 1.7f, y, C(255, 90, 30, 255));
                    DrawText("<<", size, cx + m.X / 2f + size * 0.5f, y, C(255, 90, 30, 255));
                }
            }
            else
            {
                string val = it.Value();
                if (sel && it.Adjustable) val = $"< {val} >";
                DrawText(label, size * 0.72f, cx - colW / 2f, y + size * 0.1f, col, 3f);
                var vm = Measure(val, size * 0.72f, 3f);
                var valCol = sel ? C(255, 214, 120, 255) : C(170, 150, 128, 255);
                if (sel && menu.Capturing != null)   // waiting for a key: pulse red so it is obvious
                    valCol = MathF.Sin(_time * 12f) > 0 ? C(255, 80, 50, 255) : C(255, 190, 90, 255);
                DrawText(val, size * 0.72f, cx + colW / 2f - vm.X, y + size * 0.1f, valCol, 3f);
            }
            y += step;
        }

        if (menu.Notice != null) DrawCentered(menu.Notice, small * 0.9f, cx, top + h * 0.072f, C(255, 150, 70, 255), 3f);   // under the screen title
        string hint = menu.Capturing != null ? "PRESS A KEY        ESC  CANCEL        BACKSPACE  UNBIND"
                    : menu.AtRoot ? "UP / DOWN  SELECT        ENTER  CONFIRM"
                    : menu.Current.Title == "KEY BINDINGS" ? "UP / DOWN  SELECT        ENTER  REBIND        ESC  BACK"
                    : "UP / DOWN  SELECT        LEFT / RIGHT  ADJUST        ESC  BACK";
        DrawCentered(hint, small * 0.85f, cx, h - h * 0.065f, C(150, 136, 122, 255), 3f);
        DrawText("QNOVA  //  QUAKE-STYLE ARENA SANDBOX", small * 0.7f, 18, h - small * 1.6f, C(120, 108, 98, 255), 2f, false);
        if (started && menu.AtRoot) DrawCentered("- PAUSED -", small * 0.9f, cx, top + h * 0.09f, C(150, 60, 40, 255), 6f);
    }

    void DrawGrime(int w, int h)
    {
        // vignette
        var dark = C(0, 0, 0, 215); var none = C(0, 0, 0, 0);
        Raylib.DrawRectangleGradientV(0, 0, w, (int)(h * 0.30f), dark, none);
        Raylib.DrawRectangleGradientV(0, (int)(h * 0.62f), w, h - (int)(h * 0.62f), none, dark);
        Raylib.DrawRectangleGradientH(0, 0, (int)(w * 0.22f), h, dark, none);
        Raylib.DrawRectangleGradientH(w - (int)(w * 0.22f), 0, (int)(w * 0.22f), h, none, dark);
        // scanlines
        for (int y = 0; y < h; y += 3) Raylib.DrawRectangle(0, y, w, 1, C(0, 0, 0, 44));
        // film grain
        for (int i = 0; i < 380; i++)
        {
            int v = _rng.Next(70, 200);
            Raylib.DrawRectangle(_rng.Next(w), _rng.Next(h), 2, 1, C(v, v, v, _rng.Next(10, 40)));
        }
    }
}
