# qnova

A small C# Quake clone focused on Quake 1 movement and weapons.

- `src/Qnova.Core` – headless simulation (no graphics): swept-AABB collision, Quake ground/air movement
  (friction, accelerate, air-strafe/strafe-jumping, step-up, no-autohop jump), weapons and splash damage.
- `src/Qnova.Game` – Raylib-cs 3D frontend with a test arena and target dummies.
- `tests/Qnova.Tests` – xunit tests for movement and weapons.

```
dotnet test
dotnet run --project src/Qnova.Game
```

Controls: WASD, Space (jump, release between hops), mouse look, LMB fire, 1-7 or wheel to change weapon, R reset.

Weapons (Q1 stats): Axe, Shotgun (6x4), Double Shotgun (14x4), Nailgun (9), Super Nailgun (18),
Grenade Launcher (bouncing, 2.5s fuse), Rocket Launcher (100-120 direct, 120 splash, self-damage halved,
knockback = damage x 8 so rocket jumps work).
