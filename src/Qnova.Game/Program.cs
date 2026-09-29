using System.Numerics;
using Qnova.Core;
using Raylib_cs;

const float S = 1f / 32f;   // Quake units -> render units
static Vector3 R(Vector3 v) => v * S;

Raylib.SetConfigFlags(ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint);
Raylib.InitWindow(1280, 720, "qnova");
Raylib.DisableCursor();

var game = Arena.Build();
var spawn = game.Player.Move.Position;
var effects = new List<(Vector3 Pos, float Until, float Radius, Color Color)>();
var tracers = new List<(Vector3 A, Vector3 B, float Until)>();
float yaw = -90, pitch = 0, acc = 0;
const float sens = 0.10f;
bool fireHeld = false;

var keys = new (KeyboardKey Key, WeaponId Id)[]
{
    (KeyboardKey.One, WeaponId.Axe), (KeyboardKey.Two, WeaponId.Shotgun), (KeyboardKey.Three, WeaponId.SuperShotgun),
    (KeyboardKey.Four, WeaponId.Nailgun), (KeyboardKey.Five, WeaponId.SuperNailgun),
    (KeyboardKey.Six, WeaponId.GrenadeLauncher), (KeyboardKey.Seven, WeaponId.RocketLauncher),
};

while (!Raylib.WindowShouldClose())
{
    var md = Raylib.GetMouseDelta();
    yaw -= md.X * sens;
    pitch = Math.Clamp(pitch - md.Y * sens, -89f, 89f);
    fireHeld = Raylib.IsMouseButtonDown(MouseButton.Left);
    WeaponId? sel = null;
    foreach (var (k, id) in keys) if (Raylib.IsKeyPressed(k)) sel = id;
    var wheel = Raylib.GetMouseWheelMove();
    if (wheel != 0)
    {
        int n = WeaponDef.All.Length, cur = (int)game.Player.Current;
        sel = (WeaponId)((cur + (wheel > 0 ? 1 : n - 1)) % n);
    }
    if (Raylib.IsKeyPressed(KeyboardKey.R))
    {
        game.Player.Move.Position = spawn; game.Player.Move.Velocity = default;
        game.Player.Health = 100; game.Player.Shells = 25; game.Player.Nails = 100; game.Player.Rockets = 10;
    }

    var cmd = new UserCmd
    {
        Forward = (Raylib.IsKeyDown(KeyboardKey.W) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.S) ? 1 : 0),
        Side = (Raylib.IsKeyDown(KeyboardKey.D) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1 : 0),
        Jump = Raylib.IsKeyDown(KeyboardKey.Space),
        Yaw = yaw, Pitch = pitch,
    };
    game.Player.Move.AutoHop = false;

    acc += Math.Min(Raylib.GetFrameTime(), 0.1f);
    while (acc >= GameWorld.Dt)
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
                case EventKind.Explosion: effects.Add((e.A, (float)now + 0.35f, 120, Color.Orange)); break;
                case EventKind.Impact: effects.Add((e.A, (float)now + 0.1f, 4, Color.Yellow)); break;
                case EventKind.Tracer: tracers.Add((e.A, e.B, (float)now + 0.05f)); break;
            }
        }
        game.Events.Clear();
    }

    var p = game.Player;
    var cam = new Camera3D
    {
        Position = R(p.Eye), Target = R(p.Eye + p.Look), Up = Vector3.UnitY, FovY = 90, Projection = CameraProjection.Perspective,
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
    Raylib.DrawText("WASD move  SPACE jump  MOUSE look  LMB fire  1-7/wheel weapon  R reset", 16, 44, 16, Color.Gray);
    if (!p.Alive) Raylib.DrawText("YOU DIED - press R", 500, 340, 30, Color.Red);
    Raylib.EndDrawing();
}
Raylib.CloseWindow();
