namespace Qnova.Core;

/// <summary>Things a key or mouse button can do in the game. Menu navigation, Esc and the console key are fixed.</summary>
public enum InputAction
{
    Forward, Back, MoveLeft, MoveRight, Jump, Fire, Zoom,
    Weapon1, Weapon2, Weapon3, Weapon4, Weapon5, Weapon6, Weapon7,
    NextWeapon, PrevWeapon, Mute, Respawn,
}

/// <summary>Maps actions to key / mouse codes. Codes are plain upper-case strings ("W", "SPACE", "MOUSE1", "MWHEELUP")
/// so this stays independent of the windowing library. Each action has at most one code and each code drives at most one
/// action: binding a code that is already in use takes it from its previous action (as Quake does).</summary>
public sealed class KeyBindings
{
    static readonly (InputAction Action, string Name, string Label, string Default)[] Table =
    {
        (InputAction.Forward,     "forward",    "MOVE FORWARD",   "W"),
        (InputAction.Back,        "back",       "MOVE BACK",      "S"),
        (InputAction.MoveLeft,    "moveleft",   "MOVE LEFT",      "A"),
        (InputAction.MoveRight,   "moveright",  "MOVE RIGHT",     "D"),
        (InputAction.Jump,        "jump",       "JUMP",           "SPACE"),
        (InputAction.Fire,        "attack",     "FIRE",           "MOUSE1"),
        (InputAction.Zoom,        "zoom",       "ZOOM (HOLD)",    "MOUSE3"),
        (InputAction.Weapon1,     "weapon1",    "AXE",            "ONE"),
        (InputAction.Weapon2,     "weapon2",    "SHOTGUN",        "TWO"),
        (InputAction.Weapon3,     "weapon3",    "DOUBLE SHOTGUN", "THREE"),
        (InputAction.Weapon4,     "weapon4",    "NAILGUN",        "FOUR"),
        (InputAction.Weapon5,     "weapon5",    "SUPER NAILGUN",  "FIVE"),
        (InputAction.Weapon6,     "weapon6",    "GRENADE LAUNCHER", "SIX"),
        (InputAction.Weapon7,     "weapon7",    "ROCKET LAUNCHER", "SEVEN"),
        (InputAction.NextWeapon,  "nextweapon", "NEXT WEAPON",    "MWHEELUP"),
        (InputAction.PrevWeapon,  "prevweapon", "PREVIOUS WEAPON", "MWHEELDOWN"),
        (InputAction.Mute,        "mute",       "MUTE SOUND",     "M"),
        (InputAction.Respawn,     "respawn",    "RESPAWN",        "R"),
    };

    static readonly Dictionary<string, string> Friendly = new()
    {
        ["MOUSE1"] = "MOUSE LEFT", ["MOUSE2"] = "MOUSE RIGHT", ["MOUSE3"] = "MOUSE MIDDLE",
        ["MOUSE4"] = "MOUSE SIDE", ["MOUSE5"] = "MOUSE EXTRA",
        ["MWHEELUP"] = "WHEEL UP", ["MWHEELDOWN"] = "WHEEL DOWN",
        ["ONE"] = "1", ["TWO"] = "2", ["THREE"] = "3", ["FOUR"] = "4", ["FIVE"] = "5",
        ["SIX"] = "6", ["SEVEN"] = "7", ["EIGHT"] = "8", ["NINE"] = "9", ["ZERO"] = "0",
        ["LEFTSHIFT"] = "L SHIFT", ["RIGHTSHIFT"] = "R SHIFT", ["LEFTCONTROL"] = "L CTRL", ["RIGHTCONTROL"] = "R CTRL",
        ["LEFTALT"] = "L ALT", ["RIGHTALT"] = "R ALT", ["UP"] = "UP ARROW", ["DOWN"] = "DOWN ARROW",
        ["LEFT"] = "LEFT ARROW", ["RIGHT"] = "RIGHT ARROW", ["CAPSLOCK"] = "CAPS LOCK", ["BACKSPACE"] = "BACKSPACE",
    };

    readonly Dictionary<InputAction, string> _byAction = new();
    readonly Dictionary<string, InputAction> _byKey = new();

    /// <summary>Set by the frontend to reject codes it cannot read from the keyboard or mouse.</summary>
    public Func<string, bool>? Validator;

    /// <summary>Raised after any change (used to save the bindings).</summary>
    public event Action? Changed;

    public KeyBindings() { ResetDefaults(silent: true); }

    public static IReadOnlyList<InputAction> All { get; } = Table.Select(t => t.Action).ToArray();
    public static string Name(InputAction a) => Table.First(t => t.Action == a).Name;
    public static string Label(InputAction a) => Table.First(t => t.Action == a).Label;
    public static string DefaultKey(InputAction a) => Table.First(t => t.Action == a).Default;

    public static bool TryParseAction(string name, out InputAction action)
    {
        foreach (var t in Table)
            if (t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { action = t.Action; return true; }
        action = default;
        return false;
    }

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    /// <summary>Esc opens the menu and ` opens the console; neither can be rebound.</summary>
    public static bool IsReserved(string code) => Normalize(code) is "ESCAPE" or "GRAVE";

    /// <summary>Readable form of a code for the menu ("MOUSE1" -> "MOUSE LEFT", "ONE" -> "1").</summary>
    public static string Display(string? code) =>
        code == null ? "UNBOUND" : Friendly.TryGetValue(Normalize(code), out var f) ? f : Normalize(code);

    public string? Get(InputAction a) => _byAction.TryGetValue(a, out var c) ? c : null;

    public InputAction? ActionFor(string code) =>
        _byKey.TryGetValue(Normalize(code), out var a) ? a : null;

    /// <summary>Bind a code to an action. Returns false (and changes nothing) if the code is empty, reserved or invalid.
    /// <paramref name="displaced"/> is the action that lost the code, if it was already in use.</summary>
    public bool Bind(InputAction action, string code, out InputAction? displaced)
    {
        displaced = null;
        code = Normalize(code);
        if (code.Length == 0 || code.Contains(' ') || IsReserved(code) || (Validator != null && !Validator(code))) return false;

        if (_byKey.TryGetValue(code, out var other) && other != action)
        {
            _byAction.Remove(other);
            displaced = other;
        }
        if (_byAction.TryGetValue(action, out var old)) _byKey.Remove(old);
        _byAction[action] = code;
        _byKey[code] = action;
        Changed?.Invoke();
        return true;
    }

    public void Unbind(InputAction action)
    {
        if (!_byAction.Remove(action, out var code)) return;
        _byKey.Remove(code);
        Changed?.Invoke();
    }

    public bool UnbindKey(string code)
    {
        if (!_byKey.TryGetValue(Normalize(code), out var a)) return false;
        Unbind(a);
        return true;
    }

    public void UnbindAll()
    {
        if (_byAction.Count == 0) return;
        _byAction.Clear(); _byKey.Clear();
        Changed?.Invoke();
    }

    public void ResetDefaults() => ResetDefaults(silent: false);

    void ResetDefaults(bool silent)
    {
        _byAction.Clear(); _byKey.Clear();
        foreach (var t in Table) { _byAction[t.Action] = t.Default; _byKey[t.Default] = t.Action; }
        if (!silent) Changed?.Invoke();
    }

    /// <summary>One line per action: "bind KEY action", or "unbind action" for one deliberately left unbound.
    /// Suitable for replaying through the console.</summary>
    public IEnumerable<string> ToConfigLines() =>
        Table.Select(t => _byAction.TryGetValue(t.Action, out var code) ? $"bind {code} {t.Name}" : $"unbind {t.Name}");

    /// <summary>Apply a saved config on top of the defaults (so actions added in later versions keep their default key,
    /// and a deliberate "unbind" persists). Unknown lines and codes the frontend rejects are skipped.
    /// Returns the number of lines applied; an empty or garbled file changes nothing.</summary>
    public int LoadConfig(IEnumerable<string> lines)
    {
        var unbinds = new List<InputAction>();
        var binds = new List<(InputAction Action, string Code)>();
        foreach (var raw in lines)
        {
            var t = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (t.Length == 3 && t[0].Equals("bind", StringComparison.OrdinalIgnoreCase) && TryParseAction(t[2], out var a)) binds.Add((a, t[1]));
            else if (t.Length == 2 && t[0].Equals("unbind", StringComparison.OrdinalIgnoreCase) && TryParseAction(t[1], out var u)) unbinds.Add(u);
        }
        if (binds.Count + unbinds.Count == 0) return 0;

        var savedChanged = Changed; Changed = null;
        ResetDefaults(silent: true);
        int n = 0;
        foreach (var u in unbinds) { Unbind(u); n++; }
        foreach (var (a, code) in binds) if (Bind(a, code, out _)) n++;
        Changed = savedChanged;
        Changed?.Invoke();
        return n;
    }
}
