using System.Globalization;

namespace Qnova.Core;

public sealed class Cvar
{
    public required string Name;
    public required float Default;
    public float Value;
    public string Description = "";
    public bool Cheat;                 // can only be changed while sv_cheats is 1
    public Action<float>? OnChange;    // applies the value to the simulation
}

public sealed class ConsoleCommand
{
    public required string Name;
    public string Usage = "";
    public string Description = "";
    public bool Cheat;
    public required Action<string[]> Run;   // args exclude the command name
}

/// <summary>Quake-style console: cvars, commands, ';' separated statements, quoted args.
/// Output lines are collected in <see cref="Lines"/> for the UI to draw.</summary>
public sealed class GameConsole
{
    readonly Dictionary<string, Cvar> _cvars = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ConsoleCommand> _cmds = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Lines = new();
    public const int MaxLines = 500;
    /// <summary>Total lines ever printed; lets a UI detect new output even after old lines are trimmed.</summary>
    public long TotalPrinted { get; private set; }

    public GameConsole()
    {
        AddCvar("sv_cheats", 0, "Allow cheat commands and cheat cvars (0/1)");
        AddCommand("help", "help [name]", "Describe a command or cvar, or list how to use the console", a =>
        {
            if (a.Length == 0)
            {
                Print("Type a cvar name to see it, 'name value' to set it. Separate commands with ';'.");
                Print("cvarlist / cmdlist list everything; find <text> searches; TAB completes.");
                return;
            }
            if (_cmds.TryGetValue(a[0], out var c)) Print($"{c.Usage.OrEmpty(c.Name)} - {c.Description}{(c.Cheat ? " [cheat]" : "")}");
            else if (_cvars.TryGetValue(a[0], out var v)) Print(Describe(v));
            else Print($"No such command or cvar \"{a[0]}\"");
        });
        AddCommand("echo", "echo <text>", "Print text", a => Print(string.Join(' ', a)));
        AddCommand("cvarlist", "cvarlist [prefix]", "List cvars", a => List(_cvars.Values.Where(v => a.Length == 0 || v.Name.StartsWith(a[0], StringComparison.OrdinalIgnoreCase)).OrderBy(v => v.Name).Select(Describe)));
        AddCommand("cmdlist", "cmdlist [prefix]", "List commands", a => List(_cmds.Values.Where(c => a.Length == 0 || c.Name.StartsWith(a[0], StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.Name).Select(c => $"{c.Usage.OrEmpty(c.Name)} - {c.Description}{(c.Cheat ? " [cheat]" : "")}")));
        AddCommand("find", "find <text>", "Search command and cvar names", a =>
        {
            if (a.Length == 0) { Print("usage: find <text>"); return; }
            var hits = Names().Where(n => n.Contains(a[0], StringComparison.OrdinalIgnoreCase)).ToList();
            Print(hits.Count == 0 ? "no matches" : string.Join("  ", hits));
        });
        AddCommand("set", "set <cvar> <value>", "Set a cvar", a =>
        {
            if (a.Length < 2) Print("usage: set <cvar> <value>"); else Execute($"{Quote(a[0])} {Quote(a[1])}", echo: false);
        });
        AddCommand("toggle", "toggle <cvar>", "Flip a 0/1 cvar", a =>
        {
            if (a.Length < 1 || !_cvars.TryGetValue(a[0], out var v)) { Print("usage: toggle <cvar>"); return; }
            SetCvar(v, v.Value == 0 ? 1 : 0);
        });
        AddCommand("reset", "reset <cvar>|all", "Restore a cvar (or all cvars) to its default", a =>
        {
            if (a.Length < 1) { Print("usage: reset <cvar>|all"); return; }
            if (a[0].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var v in _cvars.Values.Where(v => v.Name != "sv_cheats")) SetValue(v, v.Default);
                Print("all cvars reset (sv_cheats left alone)");
            }
            else if (_cvars.TryGetValue(a[0], out var c)) SetCvar(c, c.Default);
            else Print($"No such cvar \"{a[0]}\"");
        });
    }

    public IEnumerable<Cvar> Cvars => _cvars.Values;
    public float Get(string name) => _cvars[name].Value;
    public bool CheatsOn => _cvars["sv_cheats"].Value != 0;

    public Cvar AddCvar(string name, float def, string desc = "", Action<float>? onChange = null, bool cheat = false)
    {
        var v = new Cvar { Name = name, Default = def, Value = def, Description = desc, OnChange = onChange, Cheat = cheat };
        _cvars[name] = v;
        onChange?.Invoke(def);
        return v;
    }

    public void AddCommand(string name, string usage, string desc, Action<string[]> run, bool cheat = false) =>
        _cmds[name] = new ConsoleCommand { Name = name, Usage = usage, Description = desc, Run = run, Cheat = cheat };

    public void Print(string line)
    {
        foreach (var l in line.Split('\n')) { Lines.Add(l); TotalPrinted++; }
        if (Lines.Count > MaxLines) Lines.RemoveRange(0, Lines.Count - MaxLines);
    }

    void List(IEnumerable<string> items) { foreach (var i in items) Print(i); }

    IEnumerable<string> Names() => _cmds.Keys.Concat(_cvars.Keys).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    /// <summary>Names starting with the prefix (for TAB completion).</summary>
    public List<string> Complete(string prefix) =>
        Names().Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

    static string Describe(Cvar v) =>
        $"{v.Name} = {Fmt(v.Value)} (default {Fmt(v.Default)}){(v.Cheat ? " [cheat]" : "")}{(v.Description.Length > 0 ? " - " + v.Description : "")}";

    public static string Fmt(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
    static string Quote(string s) => s.Contains(' ') || s.Length == 0 ? $"\"{s}\"" : s;

    /// <summary>Split a line on ';' (outside quotes), dropping // comments.</summary>
    public static List<string> SplitStatements(string line)
    {
        var res = new List<string>(); var cur = new System.Text.StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') q = !q;
            if (!q && c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
            if (!q && c == ';') { res.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(c);
        }
        res.Add(cur.ToString());
        return res.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }

    public static string[] Tokenize(string stmt)
    {
        var res = new List<string>(); var cur = new System.Text.StringBuilder(); bool q = false, any = false;
        foreach (char c in stmt)
        {
            if (c == '"') { q = !q; any = true; continue; }
            if (!q && char.IsWhiteSpace(c)) { if (cur.Length > 0 || any) res.Add(cur.ToString()); cur.Clear(); any = false; continue; }
            cur.Append(c);
        }
        if (cur.Length > 0 || any) res.Add(cur.ToString());
        return res.ToArray();
    }

    public void Execute(string line, bool echo = true)
    {
        if (echo) Print("] " + line);
        foreach (var stmt in SplitStatements(line))
        {
            var t = Tokenize(stmt);
            if (t.Length == 0) continue;
            var name = t[0]; var args = t[1..];
            try
            {
                if (_cmds.TryGetValue(name, out var cmd))
                {
                    if (cmd.Cheat && !CheatsOn) Print($"\"{cmd.Name}\" is a cheat; set sv_cheats 1 first");
                    else cmd.Run(args);
                }
                else if (_cvars.TryGetValue(name, out var cv))
                {
                    if (args.Length == 0) Print(Describe(cv));
                    else if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var val)) Print($"\"{args[0]}\" is not a number");
                    else if (cv.Cheat && !CheatsOn) Print($"\"{cv.Name}\" is a cheat; set sv_cheats 1 first");
                    else SetCvar(cv, val);
                }
                else Print($"Unknown command \"{name}\"");
            }
            catch (Exception e) { Print($"error: {e.Message}"); }
        }
    }

    void SetCvar(Cvar v, float value)
    {
        if (v.Cheat && !CheatsOn) { Print($"\"{v.Name}\" is a cheat; set sv_cheats 1 first"); return; }
        SetValue(v, value);
        Print($"{v.Name} = {Fmt(v.Value)}");
    }

    void SetValue(Cvar v, float value)
    {
        v.Value = value;
        v.OnChange?.Invoke(value);
        // Turning cheats off must also undo their effects.
        if (v.Name == "sv_cheats" && value == 0)
            foreach (var c in _cvars.Values.Where(c => c.Cheat)) { c.Value = c.Default; c.OnChange?.Invoke(c.Default); }
    }
}

static class StringExt { public static string OrEmpty(this string s, string fallback) => s.Length > 0 ? s : fallback; }
