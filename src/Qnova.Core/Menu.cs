using System.Globalization;

namespace Qnova.Core;

public sealed class MenuItem
{
    public required Func<string> Label;
    public Func<string>? Value;          // shown on the right (options)
    public Action? OnSelect;             // Enter / click
    public Action<int>? OnAdjust;        // Left/Right: -1 / +1
    public bool Adjustable => OnAdjust != null;
}

public sealed class MenuScreen
{
    public required string Title;
    public readonly List<MenuItem> Items = new();
}

/// <summary>Keyboard-driven menu state machine for the splash screen (main menu + options).
/// It holds no drawing code so it can be tested; option values are read and written through console cvars.</summary>
public sealed class MenuModel
{
    readonly Stack<(MenuScreen Screen, int Selected)> _stack = new();
    public MenuScreen Current { get; private set; }
    public int Selected { get; private set; }
    public bool AtRoot => _stack.Count == 0;

    MenuModel(MenuScreen root) { Current = root; }

    public MenuItem SelectedItem => Current.Items[Selected];

    public void Push(MenuScreen s) { _stack.Push((Current, Selected)); Current = s; Selected = 0; }

    /// <summary>Move the highlight; wraps around. Returns true if it moved.</summary>
    public bool Move(int dir)
    {
        int n = Current.Items.Count;
        if (n < 2) return false;
        Selected = ((Selected + dir) % n + n) % n;
        return true;
    }

    public bool SetSelected(int i)
    {
        if (i < 0 || i >= Current.Items.Count || i == Selected) return false;
        Selected = i; return true;
    }

    /// <summary>Left/Right on an adjustable item. Returns true if the item handles it.</summary>
    public bool Adjust(int dir)
    {
        var item = SelectedItem;
        if (item.OnAdjust == null) return false;
        item.OnAdjust(dir);
        return true;
    }

    /// <summary>Enter / click. Adjustable items without a select action step forward (toggles).</summary>
    public void Select()
    {
        var item = SelectedItem;
        if (item.OnSelect != null) item.OnSelect();
        else item.OnAdjust?.Invoke(1);
    }

    /// <summary>Escape: pop back one screen. Returns false at the root (caller decides: resume or ignore).</summary>
    public bool Back()
    {
        if (_stack.Count == 0) return false;
        (Current, Selected) = _stack.Pop();
        return true;
    }

    public const int MaxBots = 4;

    /// <summary>Build the main menu and options screen for a game.
    /// <paramref name="hasStarted"/> switches "START GAME" to "RESUME GAME" once the player has entered the arena.</summary>
    public static MenuModel Create(GameWorld g, Func<bool> hasStarted, Action start, Action quit)
    {
        var options = new MenuScreen { Title = "OPTIONS" };
        var root = new MenuScreen { Title = "QNOVA" };
        var m = new MenuModel(root);

        options.Items.Add(Slider(g, "MOUSE SENSITIVITY", "sensitivity", 0.02f, 0.5f, 0.01f, "0.00"));
        options.Items.Add(Slider(g, "FIELD OF VIEW", "fov", 60f, 120f, 5f, "0"));
        options.Items.Add(Slider(g, "VOLUME", "volume", 0f, 1f, 0.1f, "0%"));
        options.Items.Add(Slider(g, "BOT SKILL", "bot_skill", 1f, 5f, 1f, "0"));
        options.Items.Add(new MenuItem
        {
            Label = () => "BOTS",
            Value = () => g.Bots.Count.ToString(CultureInfo.InvariantCulture),
            OnAdjust = dir =>
            {
                if (dir > 0 && g.Bots.Count < MaxBots) g.AddBot();
                else if (dir < 0 && g.Bots.Count > 0) g.Bots.RemoveAt(g.Bots.Count - 1);
            },
        });
        options.Items.Add(new MenuItem
        {
            Label = () => "AUTO BUNNY-HOP",
            Value = () => g.Console.Get("sv_autohop") != 0 ? "ON" : "OFF",
            OnAdjust = _ => g.Console.Execute($"sv_autohop {(g.Console.Get("sv_autohop") != 0 ? 0 : 1)}", echo: false),
        });
        options.Items.Add(new MenuItem { Label = () => "BACK", OnSelect = () => m.Back() });

        root.Items.Add(new MenuItem { Label = () => hasStarted() ? "RESUME GAME" : "START GAME", OnSelect = start });
        root.Items.Add(new MenuItem { Label = () => "OPTIONS", OnSelect = () => m.Push(options) });
        root.Items.Add(new MenuItem { Label = () => "QUIT", OnSelect = quit });
        return m;
    }

    static MenuItem Slider(GameWorld g, string label, string cvar, float min, float max, float step, string fmt)
    {
        return new MenuItem
        {
            Label = () => label,
            Value = () => g.Console.TryGet(cvar, out var v) ? Format(v, fmt) : "n/a",
            OnAdjust = dir =>
            {
                if (!g.Console.TryGet(cvar, out var v)) return;
                float next = Math.Clamp(MathF.Round((v + dir * step) / step) * step, min, max);
                g.Console.Execute($"{cvar} {next.ToString("0.####", CultureInfo.InvariantCulture)}", echo: false);
            },
        };
    }

    static string Format(float v, string fmt) =>
        fmt == "0%" ? $"{MathF.Round(v * 100):0}%" : v.ToString(fmt, CultureInfo.InvariantCulture);
}
