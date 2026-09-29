using System.Numerics;
using Qnova.Core;
using Raylib_cs;

const float S = 1f / 32f;   // Quake units -> render units
static Vector3 R(Vector3 v) => v * S;

// Developer flags (used to capture screenshots headlessly): --start, --paused, --options, --console, --lock-look, --pos "x y z", --yaw, --pitch, --exec "<console line>", --shot <png> [--shot-after <sec>]
string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
bool devLock = args.Contains("--lock-look");   // ignore mouse look (keeps screenshots framed)
bool devStart = args.Contains("--start"), devConsole = args.Contains("--console");
string? devExec = Arg("--exec"), shotPath = Arg("--shot"), devPos = Arg("--pos");
float? DevF(string n) => float.TryParse(Arg(n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
long frameCount = 0;
double shotAfter = double.TryParse(Arg("--shot-after"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sa) ? sa : 2.0;

Raylib.SetConfigFlags(ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint);
Raylib.InitWindow(1280, 720, "qnova");
Raylib.InitAudioDevice();
Raylib.SetExitKey(KeyboardKey.Null);   // Esc backs out of menus / closes the console / pauses

// Sound effects are synthesized by Qnova.Core; each id gets a small pool so rapid fire can overlap.
const int PoolSize = 4;
var audioOk = Raylib.IsAudioDeviceReady();
var sounds = new Dictionary<SoundId, (Sound[] Pool, int[] Next)>();
if (audioOk)
    foreach (var id in Enum.GetValues<SoundId>())
    {
        var bytes = SoundSynth.ToWav(SoundSynth.Generate(id));
        var wave = Raylib.LoadWaveFromMemory(".wav", bytes);
        var pool = new Sound[PoolSize];
        for (int i = 0; i < PoolSize; i++) pool[i] = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);
        sounds[id] = (pool, new int[1]);
    }
bool muted = false;

void Play(SoundId id, Vector3 at, Vector3 listener, float volume = 1f)
{
    if (!audioOk || muted || !sounds.TryGetValue(id, out var e)) return;
    float dist = Vector3.Distance(at, listener);
    float v = volume * Math.Clamp(1f - dist / 2500f, 0f, 1f);
    if (v <= 0.01f) return;
    var snd = e.Pool[e.Next[0]++ % PoolSize];
    Raylib.SetSoundVolume(snd, v);
    Raylib.PlaySound(snd);
}

var game = Arena.Build();
var ui = new ConsoleUi(game.Console);
bool quit = false;

// client-side variables and commands
float sens = 0.10f, fov = 90f, timescale = 1f;
bool plainBlocks = false;
game.Console.AddCvar("sensitivity", sens, "Mouse sensitivity (degrees per pixel)", v => sens = Math.Max(0f, v));
game.Console.AddCvar("fov", fov, "Vertical field of view in degrees", v => fov = Math.Clamp(v, 30f, 140f));
game.Console.AddCvar("volume", 1f, "Master volume 0-1", v => { if (audioOk) Raylib.SetMasterVolume(Math.Clamp(v, 0f, 1f)); });
game.Console.AddCvar("r_plain", 0f, "Render the map as plain flat-shaded blocks (0/1)", v => plainBlocks = v != 0);
game.Console.AddCvar("host_timescale", 1f, "Game speed multiplier (slow-mo / fast-forward)", v => timescale = Math.Clamp(v, 0.05f, 8f), cheat: true);
game.Console.AddCommand("quit", "quit", "Exit the game", _ => quit = true);
game.Console.AddCommand("mute", "mute", "Toggle sound", _ => { muted = !muted; game.Console.Print(muted ? "sound off" : "sound on"); });
game.Console.AddCommand("clear", "clear", "Clear the console", _ => game.Console.Lines.Clear());
game.Console.Print("qnova console - type 'help' or 'cvarlist'. Cheats: 'sv_cheats 1'.");

// Splash / main menu. The game world exists behind it but is frozen until you start.
bool inMenu = true, started = false, skipMouse = false;
long menuEnteredFrame = -1;   // frame on which Esc paused the game (that same Esc must not also resume it)
void PlayUi(SoundId id, float volume = 0.8f) => Play(id, Vector3.Zero, Vector3.Zero, volume);
var splash = new Splash();
splash.Exploded += first => PlayUi(SoundId.Explosion, first ? 0.9f : 0.3f);
var menu = MenuModel.Create(game, () => started, () =>
{
    started = true; inMenu = false; skipMouse = true;
    Raylib.DisableCursor();
}, () => quit = true);

var mapRenderer = new MapRenderer();
var dynLights = new List<(Vector3 Pos, Vector3 Color, float Radius, float Start, float Duration)>();   // explosions and muzzle flashes
var effects = new List<(Vector3 Pos, float Until, float Radius, Color Color)>();
var tracers = new List<(Vector3 A, Vector3 B, float Until)>();
float yaw = -90, pitch = 0, acc = 0;
bool fireHeld = false;
float hurtUntil = 0;
long seenLines = game.Console.TotalPrinted;
var feed = new List<(string Text, float Until)>();

// ---- key bindings: codes are upper-case strings ("W", "SPACE", "MOUSE1", "MWHEELUP"); KeyboardKey names double as codes ----
var keyCache = new Dictionary<string, KeyboardKey?>();
KeyboardKey? KeyOf(string code)
{
    if (keyCache.TryGetValue(code, out var cached)) return cached;
    KeyboardKey? found = null;
    if (!code.All(char.IsDigit) && Enum.TryParse<KeyboardKey>(code, true, out var k) && Enum.IsDefined(k) && k != KeyboardKey.Null) found = k;
    keyCache[code] = found;
    return found;
}
MouseButton? MouseOf(string code) => code switch
{
    "MOUSE1" => MouseButton.Left, "MOUSE2" => MouseButton.Right, "MOUSE3" => MouseButton.Middle,
    "MOUSE4" => MouseButton.Side, "MOUSE5" => MouseButton.Extra, _ => null,
};
bool IsValidCode(string code) => MouseOf(code) != null || code is "MWHEELUP" or "MWHEELDOWN" || KeyOf(code) != null;
bool CodeDown(string code) => MouseOf(code) is { } mb ? Raylib.IsMouseButtonDown(mb) : KeyOf(code) is { } k && Raylib.IsKeyDown(k);
bool CodePressed(string code, float wheel) =>
    code == "MWHEELUP" ? wheel > 0 : code == "MWHEELDOWN" ? wheel < 0
    : MouseOf(code) is { } mb ? Raylib.IsMouseButtonPressed(mb) : KeyOf(code) is { } k && Raylib.IsKeyPressed(k);
bool ActionDown(InputAction a) => game.Bindings.Get(a) is { } c && CodeDown(c);
bool ActionPressed(InputAction a, float wheel) => game.Bindings.Get(a) is { } c && CodePressed(c, wheel);
string KeyName(InputAction a) => KeyBindings.Display(game.Bindings.Get(a));

game.Bindings.Validator = IsValidCode;
// Bindings persist in <config dir>/qnova/bindings.cfg (~/.config on Linux, %APPDATA% on Windows); --no-config skips it.
// (QNOVA_CONFIG_DIR overrides it.) GetFolderPath can return "" on minimal systems, so fall back rather than writing to the cwd.
string ConfigDir()
{
    var over = Environment.GetEnvironmentVariable("QNOVA_CONFIG_DIR");
    if (!string.IsNullOrEmpty(over)) return over;
    var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    if (!string.IsNullOrEmpty(xdg)) return Path.Combine(xdg, "qnova");
    foreach (var f in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
    {
        var p = Environment.GetFolderPath(f);
        if (!string.IsNullOrEmpty(p)) return Path.Combine(p, "qnova");
    }
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return !string.IsNullOrEmpty(home) ? Path.Combine(home, ".config", "qnova") : Path.Combine(AppContext.BaseDirectory, "config");
}
string bindingsFile = Path.Combine(ConfigDir(), "bindings.cfg");
if (!args.Contains("--no-config"))
{
    try { if (File.Exists(bindingsFile)) game.Bindings.LoadConfig(File.ReadAllLines(bindingsFile)); }
    catch (Exception e) { game.Console.Print($"couldn't read bindings: {e.Message}"); }
    game.Bindings.Changed += () =>
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(bindingsFile)!); File.WriteAllLines(bindingsFile, game.Bindings.ToConfigLines()); }
        catch (Exception e) { game.Console.Print($"couldn't save bindings: {e.Message}"); }
    };
}

if (devStart) { started = true; inMenu = false; Raylib.DisableCursor(); }
if (args.Contains("--paused")) started = true;               // show the pause variant of the menu
if (args.Contains("--options")) { menu.SetSelected(1); menu.Select(); menu.SetSelected(4); }
if (args.Contains("--keybinds"))                       // open Options > Key Bindings (add --capture to wait for a key on JUMP)
{
    menu.SetSelected(1); menu.Select(); menu.SetSelected(7); menu.Select(); menu.SetSelected(4);
    if (args.Contains("--capture")) menu.Select();
}
if (devPos != null)
{
    var pp = devPos.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    game.Player.Move.Position = new Vector3(pp[0], pp[1], pp[2]);
}
if (DevF("--yaw") is float dy) yaw = dy;
if (DevF("--pitch") is float dp) pitch = dp;
if (devExec != null) game.Console.Execute(devExec, echo: false);
if (devConsole) ui.Toggle();

while (!quit && !Raylib.WindowShouldClose())
{
    frameCount++;
    if (inMenu)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        splash.Update(Raylib.GetFrameTime(), sw, sh);

        if (menu.Capturing != null)
        {
            // Rebinding: the next key, mouse button or wheel notch becomes the binding. Esc cancels, Backspace/Delete unbinds.
            string? code = null; bool cancel = false, unbind = false;
            int kp;
            while ((kp = (int)Raylib.GetKeyPressed()) != 0)
            {
                var key = (KeyboardKey)kp;
                if (key == KeyboardKey.Escape) cancel = true;
                else if (key == KeyboardKey.Backspace || key == KeyboardKey.Delete) unbind = true;
                else code ??= key.ToString().ToUpperInvariant();
            }
            foreach (var (mb, name) in new[] { (MouseButton.Left, "MOUSE1"), (MouseButton.Right, "MOUSE2"), (MouseButton.Middle, "MOUSE3"), (MouseButton.Side, "MOUSE4"), (MouseButton.Extra, "MOUSE5") })
                if (Raylib.IsMouseButtonPressed(mb)) code ??= name;
            float wh = Raylib.GetMouseWheelMove();
            if (wh > 0) code ??= "MWHEELUP"; else if (wh < 0) code ??= "MWHEELDOWN";

            if (cancel) { menu.CancelCapture(); PlayUi(SoundId.MenuMove); }
            else if (unbind) { menu.UnbindCaptured(); PlayUi(SoundId.MenuSelect); }
            else if (code != null && menu.Capture(code)) PlayUi(SoundId.MenuSelect);
        }
        else
        {
            bool Pressed(KeyboardKey k) => Raylib.IsKeyPressed(k) || Raylib.IsKeyPressedRepeat(k);
            if (Pressed(KeyboardKey.Down) || Pressed(KeyboardKey.S)) { if (menu.Move(1)) PlayUi(SoundId.MenuMove); }
            if (Pressed(KeyboardKey.Up) || Pressed(KeyboardKey.W)) { if (menu.Move(-1)) PlayUi(SoundId.MenuMove); }
            if (Pressed(KeyboardKey.Left) || Pressed(KeyboardKey.A)) { if (menu.Adjust(-1)) PlayUi(SoundId.MenuMove); }
            if (Pressed(KeyboardKey.Right) || Pressed(KeyboardKey.D)) { if (menu.Adjust(1)) PlayUi(SoundId.MenuMove); }
            float scroll = Raylib.GetMouseWheelMove();
            if (scroll != 0 && menu.Move(scroll > 0 ? -1 : 1)) PlayUi(SoundId.MenuMove, 0.5f);
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsKeyPressed(KeyboardKey.Space))
            { PlayUi(SoundId.MenuSelect); menu.Select(); }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) && menuEnteredFrame != frameCount - 1)
            {
                if (menu.Back()) PlayUi(SoundId.MenuMove);
                else if (started) { inMenu = false; skipMouse = true; Raylib.DisableCursor(); }   // Esc at the pause menu resumes
            }

            // mouse: hover highlights, click activates (left half of a value row decreases, right half increases)
            var mp = Raylib.GetMousePosition();
            var rects = splash.ItemRects;
            bool mouseMoved = Raylib.GetMouseDelta() != Vector2.Zero;
            foreach (var (rect, idx) in rects)
            {
                if (!Raylib.CheckCollisionPointRec(mp, rect)) continue;
                if (mouseMoved && menu.SetSelected(idx)) PlayUi(SoundId.MenuMove, 0.5f);
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && idx == menu.Selected)
                {
                    var item = menu.SelectedItem;
                    if (item.Value != null && item.Adjustable) menu.Adjust(mp.X < rect.X + rect.Width / 2f ? -1 : 1);
                    else menu.Select();
                    PlayUi(SoundId.MenuSelect);
                }
            }
            // The Enter/Space/click that started a capture is still queued as a key press; drop it so it isn't captured.
            if (menu.Capturing != null) while (Raylib.GetKeyPressed() != 0) { }
        }

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        splash.Draw(sw, sh, menu, started);
        Raylib.EndDrawing();
        if (shotPath != null && Raylib.GetTime() >= shotAfter) { Raylib.TakeScreenshot(shotPath); Console.WriteLine($"[dev] shot {shotPath}: {frameCount} frames in {Raylib.GetTime():0.0}s"); quit = true; }
        continue;
    }

    if (Raylib.IsKeyPressed(KeyboardKey.Grave)) ui.Toggle();
    if (Raylib.IsKeyPressed(KeyboardKey.Escape))
    {
        if (ui.Open) ui.Close();
        else { inMenu = true; menuEnteredFrame = frameCount; Raylib.EnableCursor(); continue; }   // Esc in game = pause menu
    }
    if (ui.Open) ui.Update();
    bool paused = ui.Open;   // game input and simulation freeze while the console is down

    var md = paused || skipMouse || devLock ? default : Raylib.GetMouseDelta();
    skipMouse = false;
    yaw -= md.X * sens;
    pitch = Math.Clamp(pitch - md.Y * sens, -89f, 89f);
    fireHeld = !paused && ActionDown(InputAction.Fire);
    WeaponId? sel = null;
    float wheel = paused ? 0 : Raylib.GetMouseWheelMove();
    if (!paused)
        for (int wi = 0; wi < WeaponDef.All.Length; wi++)
            if (ActionPressed(InputAction.Weapon1 + wi, wheel)) sel = (WeaponId)wi;
    int cycle = paused ? 0 : (ActionPressed(InputAction.NextWeapon, wheel) ? 1 : 0) - (ActionPressed(InputAction.PrevWeapon, wheel) ? 1 : 0);
    if (cycle != 0)
    {
        int n = WeaponDef.All.Length, cur = (int)game.Player.Current;
        for (int i = 1; i <= n; i++)   // next (or previous) weapon you actually own
        {
            var cand = (WeaponId)((cur + (cycle > 0 ? i : n - i)) % n);
            if (game.Player.Owned.Contains(cand)) { sel = cand; break; }
        }
    }
    if (!paused && ActionPressed(InputAction.Mute, wheel)) game.Console.Execute("mute", echo: false);
    if (!paused && ActionPressed(InputAction.Respawn, wheel)) game.Respawn();

    var cmd = new UserCmd { Yaw = yaw, Pitch = pitch };
    if (!paused)
    {
        cmd.Forward = (ActionDown(InputAction.Forward) ? 1 : 0) - (ActionDown(InputAction.Back) ? 1 : 0);
        cmd.Side = (ActionDown(InputAction.MoveRight) ? 1 : 0) - (ActionDown(InputAction.MoveLeft) ? 1 : 0);
        cmd.Jump = ActionDown(InputAction.Jump);
    }

    if (!paused) acc += Math.Min(Raylib.GetFrameTime(), 0.1f) * timescale;
    while (!paused && acc >= GameWorld.Dt)
    {
        acc -= GameWorld.Dt;
        game.Tick(cmd, fireHeld, sel); sel = null;
        // dummies: apply knockback velocity with friction so they get thrown by rockets
        foreach (var t in game.Targets)
        {
            var np = t.Origin + t.Velocity * GameWorld.Dt;
            if (game.Map.IsEmpty(np, t.Half)) t.Origin = np; else t.Velocity = default;
            t.Velocity *= 0.95f;
            if (t.Alive == false) t.Velocity = default;
        }
        foreach (var e in game.Events)
        {
            double now = Raylib.GetTime();
            switch (e.Kind)
            {
                case EventKind.Shot:
                    Play(SoundSynth.ForWeapon((WeaponId)e.Arg), e.A, game.Player.Eye, 0.8f);
                    if (e.Arg != (int)WeaponId.Axe) dynLights.Add((e.A, new Vector3(1.7f, 1.3f, 0.7f), 520f, (float)now, 0.08f));
                    break;
                case EventKind.DryFire: Play(SoundId.DryFire, e.A, game.Player.Eye, 0.6f); break;
                case EventKind.Bounce: Play(SoundId.Bounce, e.A, game.Player.Eye, 0.7f); break;
                case EventKind.Explosion:
                    Play(SoundId.Explosion, e.A, game.Player.Eye);
                    dynLights.Add((e.A, new Vector3(3.0f, 1.6f, 0.7f), 1200f, (float)now, 0.55f));
                    effects.Add((e.A, (float)now + 0.35f, 120, Color.Orange)); break;
                case EventKind.Pickup: Play(SoundSynth.ForPickup((PickupKind)e.Arg), e.A, game.Player.Eye, e.B.X == 1 ? 1f : 0.5f); break;
                case EventKind.ItemRespawn: Play(SoundId.ItemRespawn, e.A, game.Player.Eye, 0.4f); break;
                case EventKind.Hurt when e.Arg == 1: hurtUntil = (float)now + 0.25f; break;
                case EventKind.Impact: effects.Add((e.A, (float)now + 0.1f, 4, Color.Yellow)); break;
                case EventKind.Tracer: tracers.Add((e.A, e.B, (float)now + 0.05f)); break;
            }
        }
        game.Events.Clear();
    }

    var p = game.Player;
    var cam = new Camera3D
    {
        Position = R(p.Eye), Target = R(p.Eye + p.Look), Up = Vector3.UnitY, FovY = fov, Projection = CameraProjection.Perspective,
    };
    float now2 = (float)Raylib.GetTime();
    effects.RemoveAll(e => e.Until < now2); tracers.RemoveAll(t => t.Until < now2);

    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(20, 20, 26, 255));
    Raylib.BeginMode3D(cam);
    // --- lit world: gather lights, then draw solids + decor with the map shader ---
    var lightSrcs = new List<LightSrc>(64);
    foreach (var l in game.Lights)
    {
        float fl = l.Flicker ? 0.82f + 0.18f * MathF.Sin(now2 * 13f + l.Position.X) * MathF.Sin(now2 * 5.1f + l.Position.Z) : 1f;
        lightSrcs.Add(new LightSrc(l.Position, l.Color * fl, l.Radius));
    }
    foreach (var k in game.Pickups)
        if (k.Active)
        {
            var pc = k.Kind switch { PickupKind.Health => new Vector3(0.2f, 0.9f, 0.35f), PickupKind.Shells => new Vector3(0.9f, 0.5f, 0.12f), PickupKind.Nails => new Vector3(0.6f, 0.6f, 0.7f), PickupKind.Rockets => new Vector3(0.9f, 0.2f, 0.15f), _ => new Vector3(1f, 0.85f, 0.2f) };
            lightSrcs.Add(new LightSrc(k.Position + new Vector3(0, 20, 0), pc, k.Kind == PickupKind.Weapon ? 260f : 190f));
        }
    foreach (var pr in game.Projectiles)
        if (pr.Kind != ProjectileKind.Nail) lightSrcs.Add(new LightSrc(pr.Pos, pr.Kind == ProjectileKind.Rocket ? new Vector3(1.6f, 0.8f, 0.3f) : new Vector3(0.4f, 1.0f, 0.3f), 380f));
    dynLights.RemoveAll(d => now2 - d.Start > d.Duration);
    foreach (var d in dynLights)
    {
        float t01 = (now2 - d.Start) / d.Duration;
        lightSrcs.Add(new LightSrc(d.Pos, d.Color * (1f - t01) * (1f - t01), d.Radius * (0.6f + 0.4f * t01)));
    }
    mapRenderer.Frame(p.Eye, lightSrcs, now2, plainBlocks);

    mapRenderer.Begin();
    foreach (var sol in game.Map.Solids)
    {
        var mat = SurfaceRules.For(sol, Arena.Height);
        MapRenderer.Box(sol, mat, MapRenderer.Palette(mat, sol));
    }
    foreach (var dc in game.Decor)
    {
        var col = dc.Surface == Surface.Emissive ? new Color((byte)dc.Color.X, (byte)dc.Color.Y, (byte)dc.Color.Z, (byte)255) : MapRenderer.Palette(dc.Surface, dc.Box);
        MapRenderer.Box(dc.Box, dc.Surface, col);
    }
    foreach (var t in game.Targets)
        if (t.Alive) MapRenderer.Box(Aabb.FromCenter(t.Origin, t.Half), Surface.Flat, new Color(190, 70 + t.Health, 60, 255));
    mapRenderer.End();
    float tnow = (float)Raylib.GetTime();
    for (int i = 0; i < game.Pickups.Count; i++)
    {
        var k = game.Pickups[i];
        var floor = k.Position - new Vector3(0, 15f, 0);
        Raylib.DrawCubeV(R(floor), new Vector3(1.1f, 0.06f, 1.1f), k.Active ? new Color(60, 60, 70, 255) : new Color(35, 35, 40, 255));   // pad
        if (!k.Active) continue;
        var col = k.Kind switch
        {
            PickupKind.Health => new Color(60, 210, 90, 255),
            PickupKind.Shells => new Color(235, 140, 30, 255),
            PickupKind.Nails => new Color(180, 180, 195, 255),
            PickupKind.Rockets => new Color(210, 60, 50, 255),
            _ => new Color(245, 215, 50, 255),
        };
        float sz = k.Kind == PickupKind.Weapon ? 1.0f : 0.7f;
        var at = R(k.Position + new Vector3(0, 8 + MathF.Sin(tnow * 2.5f + i) * 4f, 0));
        Raylib.DrawCubeV(at, new Vector3(sz, sz, sz), col);
        Raylib.DrawCubeWiresV(at, new Vector3(sz, sz, sz), new Color(20, 20, 20, 255));
    }
    foreach (var b in game.Bots)
    {
        var bp = b.Body;
        if (!bp.Alive) continue;
        var body = new Color(70, 120, 210, 255);
        mapRenderer.Begin();
        MapRenderer.Box(Aabb.FromCenter(bp.Move.Position, MoveVars.Half), Surface.Flat, body);
        MapRenderer.Box(Aabb.FromCenter(bp.Move.Position + new Vector3(0, 40, 0), new Vector3(8, 8, 8)), Surface.Flat, new Color(225, 195, 165, 255));
        mapRenderer.End();
        Raylib.DrawSphere(R(bp.Move.Position + new Vector3(0, 34, 0)), 0.32f, new Color(230, 200, 170, 255));
        var look = bp.Look;
        Raylib.DrawLine3D(R(bp.Eye), R(bp.Eye + look * 40f), Color.Red);   // gun barrel: shows where it is aiming
        Raylib.DrawCubeV(R(bp.Eye + look * 22f), new Vector3(0.12f, 0.12f, 0.12f) + Vector3.Abs(look) * 0.5f, new Color(40, 40, 40, 255));
    }
    foreach (var pr in game.Projectiles)
        Raylib.DrawSphere(R(pr.Pos), pr.Kind == ProjectileKind.Nail ? 0.05f : 0.15f, pr.Kind == ProjectileKind.Nail ? Color.Yellow : pr.Kind == ProjectileKind.Rocket ? Color.Red : Color.DarkGreen);
    foreach (var (pos, until, radius, color) in effects) Raylib.DrawSphere(R(pos), radius * S * (1 - (until - now2)), Raylib.Fade(color, 0.6f));
    foreach (var (a, b, _) in tracers) Raylib.DrawLine3D(R(a + p.Look * 8 + new Vector3(0, -6, 0)), R(b), Color.Yellow);
    Raylib.EndMode3D();

    foreach (var b in game.Bots)
    {
        var bp = b.Body;
        if (!bp.Alive) continue;
        var sp = Raylib.GetWorldToScreen(R(bp.Move.Position + new Vector3(0, 52, 0)), cam);
        var toBot = bp.Move.Position - p.Eye;
        if (Vector3.Dot(toBot, p.Look) <= 0) continue;   // behind the camera
        int bw = 60;
        Raylib.DrawRectangle((int)sp.X - bw / 2, (int)sp.Y, bw, 6, new Color(30, 30, 30, 200));
        Raylib.DrawRectangle((int)sp.X - bw / 2, (int)sp.Y, bw * Math.Clamp(bp.Health, 0, 100) / 100, 6, new Color(220, 60, 60, 255));
        Raylib.DrawText(bp.Name, (int)sp.X - bw / 2, (int)sp.Y - 18, 16, Color.White);
    }
    if (hurtUntil > now2) Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(200, 0, 0, (int)(110 * Math.Min(1f, (hurtUntil - now2) / 0.25f))));

    var w = WeaponDef.Get(p.Current);
    float speed = MathF.Sqrt(p.Move.Velocity.X * p.Move.Velocity.X + p.Move.Velocity.Z * p.Move.Velocity.Z);
    Raylib.DrawLine(640 - 8, 360, 640 + 8, 360, Color.White); Raylib.DrawLine(640, 352, 640, 368, Color.White);
    Raylib.DrawText($"HP {p.Health}   {w.Name}   shells {p.Shells}  nails {p.Nails}  rockets {p.Rockets}   frags {p.Frags}", 16, 680, 22, Color.White);
    Raylib.DrawText($"speed {speed:0}  {(p.Move.OnGround ? "ground" : "air")}", 16, 16, 22, Color.White);
    Raylib.DrawText($"{KeyName(InputAction.Forward)}/{KeyName(InputAction.MoveLeft)}/{KeyName(InputAction.Back)}/{KeyName(InputAction.MoveRight)} move  {KeyName(InputAction.Jump)} jump  MOUSE look  {KeyName(InputAction.Fire)} fire  {KeyName(InputAction.PrevWeapon)}/{KeyName(InputAction.NextWeapon)} weapon  {KeyName(InputAction.Mute)} mute  {KeyName(InputAction.Respawn)} reset  ~ console  ESC menu", 16, 44, 16, Color.Gray);
    if (!p.Alive)
    {
        float left = Math.Max(0f, p.RespawnAt - game.Time);
        Raylib.DrawText($"YOU DIED - respawning in {left:0.0}s", 400, 340, 30, Color.Red);
    }

    // weapon bar: owned guns bright, current one boxed
    string[] short_ = { "Axe", "SG", "SSG", "NG", "SNG", "GL", "RL" };
    for (int i = 0; i < short_.Length; i++)
    {
        int x = 16 + i * 74, y = 640;
        bool owned = p.Owned.Contains((WeaponId)i), cur = p.Current == (WeaponId)i;
        if (cur) Raylib.DrawRectangleLines(x - 4, y - 3, 68, 26, Color.Yellow);
        var kn = KeyName(InputAction.Weapon1 + i);
        if (kn.Length > 4) kn = kn[..4];   // keep the bar compact for long key names
        Raylib.DrawText($"{kn} {short_[i]}", x, y, 20, owned ? (cur ? Color.Yellow : Color.White) : new Color(90, 90, 90, 255));
    }

    // scoreboard (top right)
    int sy = 16, sx = Raylib.GetScreenWidth() - 260;
    foreach (var c in game.Combatants.OrderByDescending(c => c.Frags))
    {
        Raylib.DrawText($"{c.Name,-6} {c.Frags,3} / {c.Deaths,-3}", sx, sy, 20, c == p ? Color.Yellow : Color.White);
        sy += 22;
    }

    // recent console output (kills, command results) fades in the corner while the console is closed
    long fresh = game.Console.TotalPrinted - seenLines;
    seenLines = game.Console.TotalPrinted;
    for (long i = Math.Min(fresh, game.Console.Lines.Count); i > 0; i--)
        feed.Add((game.Console.Lines[(int)(game.Console.Lines.Count - i)], now2 + 5f));
    while (feed.Count > 8) feed.RemoveAt(0);
    feed.RemoveAll(f => f.Until < now2);
    if (!ui.Open)
        for (int i = 0; i < feed.Count; i++)
            Raylib.DrawText(feed[i].Text, 16, 90 + i * 22, 20, Raylib.Fade(Color.White, Math.Min(1f, feed[i].Until - now2)));
    if (ui.Open) ui.Draw(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
    Raylib.EndDrawing();
    if (shotPath != null && Raylib.GetTime() >= shotAfter) { Raylib.TakeScreenshot(shotPath); Console.WriteLine($"[dev] shot {shotPath}: {frameCount} frames in {Raylib.GetTime():0.0}s"); quit = true; }
}
foreach (var (pool, _) in sounds.Values) foreach (var snd in pool) Raylib.UnloadSound(snd);
if (audioOk) Raylib.CloseAudioDevice();
mapRenderer.Unload();
splash.Unload();
ui.Unload();
Raylib.CloseWindow();
