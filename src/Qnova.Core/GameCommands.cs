using System.Globalization;
using System.Numerics;

namespace Qnova.Core;

/// <summary>Registers the simulation's cvars and commands on <see cref="GameWorld.Console"/>.</summary>
public static class GameCommands
{
    static readonly (string Name, WeaponId Id)[] Aliases =
    {
        ("axe", WeaponId.Axe), ("shotgun", WeaponId.Shotgun), ("sg", WeaponId.Shotgun),
        ("supershotgun", WeaponId.SuperShotgun), ("doubleshotgun", WeaponId.SuperShotgun), ("ssg", WeaponId.SuperShotgun),
        ("nailgun", WeaponId.Nailgun), ("ng", WeaponId.Nailgun),
        ("supernailgun", WeaponId.SuperNailgun), ("sng", WeaponId.SuperNailgun),
        ("grenadelauncher", WeaponId.GrenadeLauncher), ("gl", WeaponId.GrenadeLauncher),
        ("rocketlauncher", WeaponId.RocketLauncher), ("rl", WeaponId.RocketLauncher),
    };

    public static bool TryWeapon(string s, out WeaponId id)
    {
        id = default;
        if (int.TryParse(s, out int n) && n >= 1 && n <= WeaponDef.All.Length) { id = (WeaponId)(n - 1); return true; }
        foreach (var (name, w) in Aliases) if (name.Equals(s, StringComparison.OrdinalIgnoreCase)) { id = w; return true; }
        return false;
    }

    static bool F(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    public static void Install(GameWorld g)
    {
        var c = g.Console; var p = g.Player; var s = g.Settings;

        // ---- movement variables (free to change, like Quake's sv_*) ----
        c.AddCvar("sv_gravity", s.Gravity, "Gravity in units/s^2", v => s.Gravity = v);
        c.AddCvar("sv_maxspeed", s.MaxSpeed, "Ground speed cap", v => s.MaxSpeed = v);
        c.AddCvar("sv_accelerate", s.Accelerate, "Ground acceleration", v => s.Accelerate = v);
        c.AddCvar("sv_airaccelerate", s.AirAccelerate, "Air acceleration", v => s.AirAccelerate = v);
        c.AddCvar("sv_aircap", s.AirWishCap, "Air wish-speed cap (strafe-jump limit)", v => s.AirWishCap = v);
        c.AddCvar("sv_friction", s.Friction, "Ground friction", v => s.Friction = v);
        c.AddCvar("sv_stopspeed", s.StopSpeed, "Speed below which friction is constant", v => s.StopSpeed = v);
        c.AddCvar("sv_jumpspeed", s.JumpSpeed, "Vertical speed of a jump", v => s.JumpSpeed = v);
        c.AddCvar("sv_stepheight", s.StepHeight, "Max ledge height walked up", v => s.StepHeight = v);
        c.AddCvar("sv_autohop", 0, "Hold jump to keep bunny-hopping (0/1)", v => p.Move.AutoHop = v != 0);

        // ---- cheat variables ----
        c.AddCvar("sv_infiniteammo", 0, "Weapons use no ammo (0/1)", v => g.InfiniteAmmo = v != 0, cheat: true);
        c.AddCvar("sv_god", 0, "No self damage (0/1)", v => p.God = v != 0, cheat: true);
        c.AddCvar("sv_noclip", 0, "Fly through walls (0/1)", v => p.Move.NoClip = v != 0, cheat: true);

        // ---- commands ----
        void ToggleCheat(string name, string cvar, string label)
        {
            c.AddCommand(name, $"{name} [0|1]", $"Toggle {label}", a =>
            {
                float next = a.Length > 0 && F(a[0], out var v) ? (v != 0 ? 1 : 0) : (c.Get(cvar) == 0 ? 1 : 0);
                c.Execute($"{cvar} {next}", echo: false);
            }, cheat: true);
        }
        ToggleCheat("god", "sv_god", "god mode (no self damage)");
        ToggleCheat("noclip", "sv_noclip", "noclip (fly through walls)");
        c.AddCommand("weapon", "weapon <1-7|name>", "Select a weapon", a =>
        {
            if (a.Length < 1 || !TryWeapon(a[0], out var w)) { c.Print("usage: weapon <1-7|axe|sg|ssg|ng|sng|gl|rl>"); return; }
            p.Current = w; c.Print($"weapon: {WeaponDef.Get(w).Name}");
        });
        c.AddCommand("give", "give <all|health|ammo|weapon> [amount]", "Give items", a =>
        {
            if (a.Length < 1) { c.Print("usage: give <all|health|ammo|weapon> [amount]"); return; }
            int amt = a.Length > 1 && int.TryParse(a[1], out var n) ? n : -1;
            switch (a[0].ToLowerInvariant())
            {
                case "all": p.Shells = p.Nails = p.Rockets = 200; p.Health = p.MaxHealth; c.Print("gave everything"); break;
                case "health": p.Health = amt >= 0 ? amt : p.MaxHealth; c.Print($"health {p.Health}"); break;
                case "ammo": { int k = amt >= 0 ? amt : 100; p.Shells += k; p.Nails += k; p.Rockets += k; c.Print($"+{k} of each ammo"); break; }
                case "shells": p.Shells += amt >= 0 ? amt : 25; c.Print($"shells {p.Shells}"); break;
                case "nails": p.Nails += amt >= 0 ? amt : 100; c.Print($"nails {p.Nails}"); break;
                case "rockets": p.Rockets += amt >= 0 ? amt : 10; c.Print($"rockets {p.Rockets}"); break;
                default:
                    if (TryWeapon(a[0], out var w)) { p.Owned.Add(w); p.Current = w; c.Print($"gave {WeaponDef.Get(w).Name}"); }
                    else c.Print($"don't know how to give \"{a[0]}\"");
                    break;
            }
        }, cheat: true);
        c.AddCommand("setpos", "setpos <x> <y> <z>", "Teleport (Y is up)", a =>
        {
            if (a.Length < 3 || !F(a[0], out var x) || !F(a[1], out var y) || !F(a[2], out var z)) { c.Print("usage: setpos <x> <y> <z>"); return; }
            p.Move.Position = new Vector3(x, y, z); p.Move.Velocity = default;
        }, cheat: true);
        c.AddCommand("getpos", "getpos", "Print position and velocity", a =>
        {
            var m = p.Move;
            c.Print($"pos {GameConsole.Fmt(m.Position.X)} {GameConsole.Fmt(m.Position.Y)} {GameConsole.Fmt(m.Position.Z)}  vel {GameConsole.Fmt(m.Velocity.X)} {GameConsole.Fmt(m.Velocity.Y)} {GameConsole.Fmt(m.Velocity.Z)}");
        });
        c.AddCommand("kill", "kill", "Suicide", a => { p.Health = 0; c.Print("you suicided"); });
        c.AddCommand("respawn", "respawn", "Reset player, ammo and dummies", a => { g.Respawn(); c.Print("respawned"); });
        c.AddCommand("spawn", "spawn [count]", "Spawn target dummies where you're looking", a =>
        {
            int n = a.Length > 0 && int.TryParse(a[0], out var k) ? Math.Clamp(k, 1, 20) : 1;
            var tr = g.Map.TraceBox(p.Eye, p.Eye + p.Look * 4096f, MoveVars.Half);
            for (int i = 0; i < n; i++)
                g.Targets.Add(new Target { Origin = tr.EndPos + new Vector3((i % 5) * 40f, 0, (i / 5) * 40f) });
            c.Print($"spawned {n} dummy(s)");
        }, cheat: true);
        c.AddCommand("killtargets", "killtargets", "Remove all dummies", a => { c.Print($"removed {g.Targets.Count} dummies"); g.Targets.Clear(); }, cheat: true);
        c.AddCommand("stats", "stats", "Print player status", a =>
            c.Print($"health {p.Health}  shells {p.Shells}  nails {p.Nails}  rockets {p.Rockets}  frags {p.Frags}  weapon {WeaponDef.Get(p.Current).Name}"));
    }
}
