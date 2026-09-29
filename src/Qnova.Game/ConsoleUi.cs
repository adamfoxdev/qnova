using System.Numerics;
using Qnova.Core;
using Raylib_cs;

/// <summary>Drop-down console overlay: text entry, history, TAB completion and scrollback.</summary>
sealed class ConsoleUi
{
    readonly GameConsole _c;
    readonly List<string> _history = new();
    string _input = "";
    int _histIdx, _scroll;
    public bool Open { get; private set; }

    // Raylib's built-in font is a 10px bitmap: only crisp at multiples of 10, so the fallback uses 20.
    // Prefer a real monospace TTF from the system, rasterised at the display size.
    const int FontSize = 20;
    const int LineHeight = 24;
    readonly Font _font;
    readonly bool _customFont;

    public ConsoleUi(GameConsole c)
    {
        _c = c;
        _font = Fonts.Load(FontSize, bold: false, out _customFont);
    }

    public void Unload() { if (_customFont) Raylib.UnloadFont(_font); }

    void Text(string text, float x, float y, Color color)
    {
        float spacing = _customFont ? 0f : 2f;
        Raylib.DrawTextEx(_font, text, new Vector2(x + 1, y + 1), FontSize, spacing, new Color(0, 0, 0, 255));   // shadow
        Raylib.DrawTextEx(_font, text, new Vector2(x, y), FontSize, spacing, color);
    }

    public void Toggle()
    {
        Open = !Open;
        if (Open) Raylib.EnableCursor(); else Raylib.DisableCursor();
        while (Raylib.GetCharPressed() != 0) { }   // swallow the ` that opened us
        _scroll = 0;
    }

    public void Close() { if (Open) Toggle(); }

    public void Update()
    {
        int ch;
        while ((ch = Raylib.GetCharPressed()) != 0)
            if (ch >= 32 && ch < 127 && ch != '`' && ch != '~') _input += (char)ch;

        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && _input.Length > 0)
            _input = _input[..^1];

        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
        {
            if (_input.Trim().Length > 0)
            {
                if (_history.Count == 0 || _history[^1] != _input) _history.Add(_input);
                _c.Execute(_input);
            }
            _input = ""; _histIdx = _history.Count; _scroll = 0;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Up) && _history.Count > 0)
        { _histIdx = Math.Max(0, _histIdx - 1); _input = _history[_histIdx]; }
        if (Raylib.IsKeyPressed(KeyboardKey.Down) && _history.Count > 0)
        { _histIdx = Math.Min(_history.Count, _histIdx + 1); _input = _histIdx == _history.Count ? "" : _history[_histIdx]; }

        if (Raylib.IsKeyPressed(KeyboardKey.PageUp)) _scroll += 8;
        if (Raylib.IsKeyPressed(KeyboardKey.PageDown)) _scroll = Math.Max(0, _scroll - 8);
        _scroll = Math.Clamp(_scroll + (int)Raylib.GetMouseWheelMove() * 3, 0, Math.Max(0, _c.Lines.Count - 1));

        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) Complete();
    }

    void Complete()
    {
        // Complete the word being typed at the end of the line (command or cvar names).
        int sp = _input.LastIndexOf(' ') + 1;
        var word = _input[sp..];
        if (word.Length == 0) return;
        var m = _c.Complete(word);
        if (m.Count == 0) return;
        string common = m[0];
        foreach (var n in m) { int i = 0; while (i < common.Length && i < n.Length && char.ToLowerInvariant(common[i]) == char.ToLowerInvariant(n[i])) i++; common = common[..i]; }
        _input = _input[..sp] + common + (m.Count == 1 ? " " : "");
        if (m.Count > 1) _c.Print(string.Join("  ", m));
    }

    public void Draw(int w, int h)
    {
        int ch = h / 2;
        Raylib.DrawRectangle(0, 0, w, ch, new Color(0, 0, 0, 235));
        Raylib.DrawRectangle(0, ch, w, 2, new Color(230, 110, 40, 255));

        int rows = (ch - LineHeight - 16) / LineHeight;
        int last = _c.Lines.Count - 1 - _scroll;
        for (int r = 0; r < rows && last - r >= 0; r++)
            Text(_c.Lines[last - r], 12, ch - LineHeight * 2 - 6 - r * LineHeight, new Color(235, 235, 235, 255));
        if (_scroll > 0) Text($"^ {_scroll} more (PgDn)", w - 260, 6, new Color(170, 170, 170, 255));

        bool blink = (int)(Raylib.GetTime() * 2) % 2 == 0;
        Text("] " + _input + (blink ? "_" : " "), 12, ch - LineHeight - 4, new Color(255, 190, 90, 255));
    }
}
