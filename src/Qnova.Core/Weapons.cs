namespace Qnova.Core;

public enum WeaponId { Axe, Shotgun, SuperShotgun, Nailgun, SuperNailgun, GrenadeLauncher, RocketLauncher, LightningGun, Railgun }
public enum AmmoType { None, Shells, Nails, Rockets, Cells, Slugs }

public enum FireMode { Melee, Hitscan, Nail, Grenade, Rocket, Beam, Rail }

/// <summary>Stats follow Quake 1 (damage, refire, projectile speed, spread); the Lightning Gun and Railgun use Quake 3's numbers.</summary>
public sealed record WeaponDef(
    WeaponId Id, string Name, FireMode Mode, AmmoType Ammo, int AmmoPerShot,
    float Refire, int Damage, int Pellets = 1, float SpreadX = 0, float SpreadY = 0,
    float Range = 4096, float ProjectileSpeed = 0, float SplashRadius = 0)
{
    public static readonly WeaponDef[] All =
    {
        new(WeaponId.Axe,             "Axe",              FireMode.Melee,   AmmoType.None,    0, 0.50f, 20, Range: 64),
        new(WeaponId.Shotgun,         "Shotgun",          FireMode.Hitscan, AmmoType.Shells,  1, 0.50f, 4,  6,  0.04f, 0.04f),
        new(WeaponId.SuperShotgun,    "Double Shotgun",   FireMode.Hitscan, AmmoType.Shells,  2, 0.70f, 4,  14, 0.14f, 0.08f),
        new(WeaponId.Nailgun,         "Nailgun",          FireMode.Nail,    AmmoType.Nails,   1, 0.10f, 9,  ProjectileSpeed: 1000),
        new(WeaponId.SuperNailgun,    "Super Nailgun",    FireMode.Nail,    AmmoType.Nails,   2, 0.10f, 18, ProjectileSpeed: 1000),
        new(WeaponId.GrenadeLauncher, "Grenade Launcher", FireMode.Grenade, AmmoType.Rockets, 1, 0.60f, 120, ProjectileSpeed: 600, SplashRadius: 160),
        new(WeaponId.RocketLauncher,  "Rocket Launcher",  FireMode.Rocket,  AmmoType.Rockets, 1, 0.80f, 120, ProjectileSpeed: 1000, SplashRadius: 160),
        // Q3: 8 damage every 50 ms (160 dps) out to 768 units; the rail is 100 damage, instant, pierces everything, 1.5 s refire.
        new(WeaponId.LightningGun,    "Lightning Gun",    FireMode.Beam,    AmmoType.Cells,   1, 0.05f, 8,   Range: 768),
        new(WeaponId.Railgun,         "Railgun",          FireMode.Rail,    AmmoType.Slugs,   1, 1.50f, 100, Range: 8192),
    };

    public static WeaponDef Get(WeaponId id) => All[(int)id];
}
