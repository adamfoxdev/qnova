using System.Numerics;
using Qnova.Core;
using Raylib_cs;

const float S = 1f / 32f;   // Quake units -> render units
static Vector3 R(Vector3 v) => v * S;

// Developer flags (used to capture screenshots headlessly): --start, --paused, --options, --console, --exec "<console line>", --shot <png> [--shot-after <sec>]
string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
bool devStart = args.Contains("--start"), devConsole = args.Contains("--console");
string? devExec = Arg("--exec"), shotPath = Arg("--shot");
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
game.Console.AddCvar("sensitivity", sens, "Mouse sensitivity (degrees per pixel)", v => sens = Math.Max(0f, v));
game.Console.AddCvar("fov", fov, "Vertical field of view in degrees", v => fov = Math.Clamp(v, 30f, 140f));
game.Console.AddCvar("volume", 1f, "Master volume 0-1", v => { if (audioOk) Raylib.SetMasterVolume(Math.Clamp(v, 0f, 1f)); });
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

var effects = new List<(Vector3 Pos, float Until, float Radius, Color Color)>();
var tracers = new List<(Vector3 A, Vector3 B, float Until)>();
float yaw = -90, pitch = 0, acc = 0;
bool fireHeld = false;
float hurtUntil = 0;
long seenLines = game.Console.TotalPrinted;
var feed = new List<(string Text, float Until)>();

var keys = new (KeyboardKey Key, WeaponId Id)[]
{
    (KeyboardKey.One, WeaponId.Axe), (KeyboardKey.Two, WeaponId.Shotgun), (KeyboardKey.Three, WeaponId.SuperShotgun),
    (KeyboardKey.Four, WeaponId.Nailgun), (KeyboardKey.Five, WeaponId.SuperNailgun),
    (KeyboardKey.Six, WeaponId.GrenadeLauncher), (KeyboardKey.Seven, WeaponId.RocketLauncher),
};

if (devStart) { started = true; inMenu = false; Raylib.DisableCursor(); }
if (args.Contains("--paused")) started = true;               // show the pause variant of the menu
if (args.Contains("--options")) { menu.SetSelected(1); menu.Select(); menu.SetSelected(4); }
if (devExec != null) game.Console.Execute(devExec, echo: false);
if (devConsole) ui.Toggle();

while (!quit && !Raylib.WindowShouldClose())
{
    frameCount++;
    if (inMenu)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        splash.Update(Raylib.GetFrameTime(), sw, sh);

        bool Pressed(KeyboardKey k) => Raylib.IsKeyPressed(k) || Raylib.IsKeyPressedRepeat(k);
        if (Pressed(KeyboardKey.Down) || Pressed(KeyboardKey.S)) { if (menu.Move(1)) PlayUi(SoundId.MenuMove); }
        if (Pressed(KeyboardKey.Up) || Pressed(KeyboardKey.W)) { if (menu.Move(-1)) PlayUi(SoundId.MenuMove); }
        if (Pressed(KeyboardKey.Left) || Pressed(KeyboardKey.A)) { if (menu.Adjust(-1)) PlayUi(SoundId.MenuMove); }
        if (Pressed(KeyboardKey.Right) || Pressed(KeyboardKey.D)) { if (menu.Adjust(1)) PlayUi(SoundId.MenuMove); }
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
        for (int i = 0; i < rects.Count; i++)
        {
            if (!Raylib.CheckCollisionPointRec(mp, rects[i])) continue;
            if (mouseMoved && menu.SetSelected(i)) PlayUi(SoundId.MenuMove, 0.5f);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && i == menu.Selected)
            {
                var item = menu.SelectedItem;
                if (item.Value != null && item.Adjustable) menu.Adjust(mp.X < rects[i].X + rects[i].Width / 2f ? -1 : 1);
                else menu.Select();
                PlayUi(SoundId.MenuSelect);
            }
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

    var md = paused || skipMouse ? default : Raylib.GetMouseDelta();
    skipMouse = false;
    yaw -= md.X * sens;
    pitch = Math.Clamp(pitch - md.Y * sens, -89f, 89f);
    fireHeld = !paused && Raylib.IsMouseButtonDown(MouseButton.Left);
    WeaponId? sel = null;
    if (!paused) foreach (var (k, id) in keys) if (Raylib.IsKeyPressed(k)) sel = id;
    var wheel = paused ? 0 : Raylib.GetMouseWheelMove();
    if (wheel != 0)
    {
        int n = WeaponDef.All.Length, cur = (int)game.Player.Current;
        for (int i = 1; i <= n; i++)   // next (or previous) weapon you actually own
        {
            var cand = (WeaponId)((cur + (wheel > 0 ? i : n - i)) % n);
            if (game.Player.Owned.Contains(cand)) { sel = cand; break; }
        }
    }
    if (!paused && Raylib.IsKeyPressed(KeyboardKey.M)) game.Console.Execute("mute", echo: false);
    if (!paused && Raylib.IsKeyPressed(KeyboardKey.R)) game.Respawn();

    var cmd = new UserCmd { Yaw = yaw, Pitch = pitch };
    if (!paused)
    {
        cmd.Forward = (Raylib.IsKeyDown(KeyboardKey.W) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.S) ? 1 : 0);
        cmd.Side = (Raylib.IsKeyDown(KeyboardKey.D) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1 : 0);
        cmd.Jump = Raylib.IsKeyDown(KeyboardKey.Space);
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
                case EventKind.Shot: Play(SoundSynth.ForWeapon((WeaponId)e.Arg), e.A, game.Player.Eye, 0.8f); break;
                case EventKind.DryFire: Play(SoundId.DryFire, e.A, game.Player.Eye, 0.6f); break;
                case EventKind.Bounce: Play(SoundId.Bounce, e.A, game.Player.Eye, 0.7f); break;
                case EventKind.Explosion:
                    Play(SoundId.Explosion, e.A, game.Player.Eye);
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
    foreach (var s in game.Map.Solids)
    {
        var c = R(s.Center); var sz = R(s.Max - s.Min);
        float shade = 90 + (Math.Abs(s.Center.X * 7 + s.Center.Y * 13 + s.Center.Z * 3) % 60);
        Raylib.DrawCubeV(c, sz, new Color((int)shade, (int)(shade * 0.85f), (int)(shade * 0.7f), 255));
        Raylib.DrawCubeWiresV(c, sz, new Color(30, 25, 20, 255));
    }
    foreach (var t in game.Targets)
        if (t.Alive) Raylib.DrawCubeV(R(t.Origin), R(t.Half * 2), new Color(180, 60 + t.Health, 60, 255));
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
        var c = R(bp.Move.Position); var size = R(MoveVars.Half * 2);
        var body = new Color(60, 110, 200, 255);
        Raylib.DrawCubeV(c, size, body);
        Raylib.DrawCubeWiresV(c, size, new Color(10, 20, 50, 255));
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
    Raylib.DrawText("WASD move  SPACE jump  MOUSE look  LMB fire  1-7/wheel weapon  M mute  R reset  ~ console", 16, 44, 16, Color.Gray);
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
        Raylib.DrawText($"{i + 1} {short_[i]}", x, y, 20, owned ? (cur ? Color.Yellow : Color.White) : new Color(90, 90, 90, 255));
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
splash.Unload();
ui.Unload();
Raylib.CloseWindow();
