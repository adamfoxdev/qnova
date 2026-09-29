using System.Numerics;
using Qnova.Core;
using Raylib_cs;

const float S = 1f / 32f;   // Quake units -> render units
static Vector3 R(Vector3 v) => v * S;

Raylib.SetConfigFlags(ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint);
Raylib.InitWindow(1280, 720, "qnova");
Raylib.InitAudioDevice();
Raylib.SetExitKey(KeyboardKey.Null);   // Esc closes the console (or quits when it's closed)
Raylib.DisableCursor();

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

var effects = new List<(Vector3 Pos, float Until, float Radius, Color Color)>();
var tracers = new List<(Vector3 A, Vector3 B, float Until)>();
float yaw = -90, pitch = 0, acc = 0;
bool fireHeld = false;

var keys = new (KeyboardKey Key, WeaponId Id)[]
{
    (KeyboardKey.One, WeaponId.Axe), (KeyboardKey.Two, WeaponId.Shotgun), (KeyboardKey.Three, WeaponId.SuperShotgun),
    (KeyboardKey.Four, WeaponId.Nailgun), (KeyboardKey.Five, WeaponId.SuperNailgun),
    (KeyboardKey.Six, WeaponId.GrenadeLauncher), (KeyboardKey.Seven, WeaponId.RocketLauncher),
};

while (!quit && !Raylib.WindowShouldClose())
{
    if (Raylib.IsKeyPressed(KeyboardKey.Grave)) ui.Toggle();
    if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { if (ui.Open) ui.Close(); else quit = true; }
    if (ui.Open) ui.Update();
    bool paused = ui.Open;   // game input and simulation freeze while the console is down

    var md = paused ? default : Raylib.GetMouseDelta();
    yaw -= md.X * sens;
    pitch = Math.Clamp(pitch - md.Y * sens, -89f, 89f);
    fireHeld = !paused && Raylib.IsMouseButtonDown(MouseButton.Left);
    WeaponId? sel = null;
    if (!paused) foreach (var (k, id) in keys) if (Raylib.IsKeyPressed(k)) sel = id;
    var wheel = paused ? 0 : Raylib.GetMouseWheelMove();
    if (wheel != 0)
    {
        int n = WeaponDef.All.Length, cur = (int)game.Player.Current;
        sel = (WeaponId)((cur + (wheel > 0 ? 1 : n - 1)) % n);
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
    foreach (var pr in game.Projectiles)
        Raylib.DrawSphere(R(pr.Pos), pr.Kind == ProjectileKind.Nail ? 0.05f : 0.15f, pr.Kind == ProjectileKind.Nail ? Color.Yellow : pr.Kind == ProjectileKind.Rocket ? Color.Red : Color.DarkGreen);
    foreach (var (pos, until, radius, color) in effects) Raylib.DrawSphere(R(pos), radius * S * (1 - (until - now2)), Raylib.Fade(color, 0.6f));
    foreach (var (a, b, _) in tracers) Raylib.DrawLine3D(R(a + p.Look * 8 + new Vector3(0, -6, 0)), R(b), Color.Yellow);
    Raylib.EndMode3D();

    var w = WeaponDef.Get(p.Current);
    float speed = MathF.Sqrt(p.Move.Velocity.X * p.Move.Velocity.X + p.Move.Velocity.Z * p.Move.Velocity.Z);
    Raylib.DrawLine(640 - 8, 360, 640 + 8, 360, Color.White); Raylib.DrawLine(640, 352, 640, 368, Color.White);
    Raylib.DrawText($"HP {p.Health}   {w.Name}   shells {p.Shells}  nails {p.Nails}  rockets {p.Rockets}   frags {p.Frags}", 16, 680, 22, Color.White);
    Raylib.DrawText($"speed {speed:0}  {(p.Move.OnGround ? "ground" : "air")}", 16, 16, 22, Color.White);
    Raylib.DrawText("WASD move  SPACE jump  MOUSE look  LMB fire  1-7/wheel weapon  M mute  R reset  ~ console", 16, 44, 16, Color.Gray);
    if (!p.Alive) Raylib.DrawText("YOU DIED - press R", 500, 340, 30, Color.Red);
    if (ui.Open) ui.Draw(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
    Raylib.EndDrawing();
}
foreach (var (pool, _) in sounds.Values) foreach (var snd in pool) Raylib.UnloadSound(snd);
if (audioOk) Raylib.CloseAudioDevice();
Raylib.CloseWindow();
