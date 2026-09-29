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

Controls: WASD, Space (jump, release between hops), mouse look, LMB fire, 1-7 or wheel to change weapon, M mute, R reset, ` (~) opens the console.

Weapons (Q1 stats): Axe, Shotgun (6x4), Double Shotgun (14x4), Nailgun (9), Super Nailgun (18),
Grenade Launcher (bouncing, 2.5s fuse), Rocket Launcher (100-120 direct, 120 splash, self-damage halved,
knockback = damage x 8 so rocket jumps work).

Sound: all effects (weapon fire, grenade bounce, dry-fire click, explosions) are synthesized in code by
`SoundSynth` in `Qnova.Core`, so there are no audio assets to ship. Volume falls off with distance.

## Console

Press `~` to drop the console (game pauses; Esc or `~` closes it). Type a cvar name to see it, `name value`
to set it; `;` separates commands; TAB completes; Up/Down recall history; PgUp/PgDn scroll.

- `help [name]`, `cvarlist [prefix]`, `cmdlist`, `find <text>`, `set`, `toggle`, `reset <cvar>|all`, `echo`, `clear`, `quit`
- Movement cvars: `sv_gravity`, `sv_maxspeed`, `sv_accelerate`, `sv_airaccelerate`, `sv_aircap`, `sv_friction`,
  `sv_stopspeed`, `sv_jumpspeed`, `sv_stepheight`, `sv_autohop`. Client: `sensitivity`, `fov`, `volume`.
- Gameplay commands: `weapon <1-7|name>`, `getpos`, `stats`, `respawn`, `kill`, `mute`.
- Cheats (need `sv_cheats 1`; turning it back off reverts them): `god`, `noclip`, `sv_infiniteammo`,
  `give <all|health|ammo|shells|nails|rockets|weapon> [n]`, `setpos x y z`, `spawn [n]`, `killtargets`, `host_timescale`.
