using System.Numerics;
using Raylib_cs;

/// <summary>Arcade-style floating damage numbers. Rapid hits on the same victim (the lightning gun, shotgun pellets)
/// merge into one running total that pops each time it grows, then floats up and fades after the last hit.</summary>
sealed class DamagePopups
{
    sealed class Popup
    {
        public int Victim;
        public Vector3 Pos;
        public int Damage;
        public float LastHit, Born;
        public bool Kill;
        public float Dx;          // small random sideways offset so stacked numbers don't overlap
    }

    const float MergeWindow = 0.35f;   // hits closer together than this add to the same number
    const float RiseTime = 0.95f;      // after the last hit: float up and fade over this long
    const float RiseSpeed = 75f;       // world units per second

    readonly List<Popup> _popups = new();
    readonly Random _rng = new(5);
    readonly Font _font;
    readonly bool _custom;
    const int FontSize = 44;

    public DamagePopups()
    {
        _font = Fonts.Load(FontSize, bold: true, out _custom);
    }

    public void Unload() { if (_custom) Raylib.UnloadFont(_font); }
    public void Clear() => _popups.Clear();
    public int Count => _popups.Count;

    public void Add(int victim, Vector3 pos, int damage, bool kill, float now)
    {
        if (damage <= 0) return;
        var cur = _popups.FindLast(p => p.Victim == victim && now - p.LastHit < MergeWindow && !p.Kill);
        if (cur != null)
        {
            cur.Damage += damage; cur.Pos = pos; cur.LastHit = now; cur.Kill |= kill;
            return;
        }
        _popups.Add(new Popup { Victim = victim, Pos = pos, Damage = damage, LastHit = now, Born = now, Kill = kill, Dx = ((float)_rng.NextDouble() * 2 - 1) * 26f });
    }

    /// <summary>White for chip damage, then yellow, orange and red as the number grows; kills get the biggest, reddest treatment.</summary>
    public static Color ColorFor(int damage, bool kill)
    {
        if (kill) return new Color(255, 70, 50, 255);
        if (damage < 20) return new Color(255, 255, 255, 255);
        if (damage < 45) return new Color(255, 230, 70, 255);
        if (damage < 90) return new Color(255, 150, 40, 255);
        return new Color(255, 80, 45, 255);
    }

    public static float SizeFor(int damage, bool kill) => Math.Clamp(26f + damage * 0.22f, 26f, 58f) * (kill ? 1.3f : 1f);

    public void Draw(Camera3D cam, Func<Vector3, Vector3> toRender, float now)
    {
        _popups.RemoveAll(p => now - p.LastHit > RiseTime);
        foreach (var p in _popups)
        {
            float age = now - p.LastHit;
            var world = p.Pos + new Vector3(0, 46f + age * RiseSpeed, 0);           // just above the head, drifting up
            var rp = toRender(world);
            if (Vector3.Dot(rp - cam.Position, Vector3.Normalize(cam.Target - cam.Position)) <= 0) continue;   // behind the camera
            var sp = Raylib.GetWorldToScreen(rp, cam);

            float pop = 1f + 0.55f * MathF.Max(0f, 1f - age / 0.14f);                // a quick "punch" on every new hit
            float size = SizeFor(p.Damage, p.Kill) * pop * (_custom ? 1f : 1f);
            float alpha = age < RiseTime - 0.3f ? 1f : MathF.Max(0f, (RiseTime - age) / 0.3f);
            string text = p.Kill ? $"{p.Damage}!" : p.Damage.ToString();
            var m = Raylib.MeasureTextEx(_font, text, size, 1f);
            var at = new Vector2(sp.X + p.Dx - m.X / 2f, sp.Y - m.Y / 2f);

            var outline = Raylib.Fade(new Color(0, 0, 0, 255), alpha);
            foreach (var (ox, oy) in new[] { (-2, -2), (2, -2), (-2, 2), (2, 2), (0, 3) })
                Raylib.DrawTextEx(_font, text, at + new Vector2(ox, oy), size, 1f, outline);
            Raylib.DrawTextEx(_font, text, at, size, 1f, Raylib.Fade(ColorFor(p.Damage, p.Kill), alpha));
        }
    }
}
