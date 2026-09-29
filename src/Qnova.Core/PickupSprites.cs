namespace Qnova.Core;

/// <summary>Which sprite a pickup is drawn with. The frontend paints one procedural image per id.</summary>
public enum SpriteId
{
    Health,
    Shells, Nails, Rockets, Cells, Slugs,
    Shotgun, SuperShotgun, Nailgun, SuperNailgun, GrenadeLauncher, RocketLauncher, LightningGun, Railgun,
    Axe,
}

public static class PickupSprites
{
    public static SpriteId For(PickupKind kind, WeaponId weapon) => kind switch
    {
        PickupKind.Health => SpriteId.Health,
        PickupKind.Shells => SpriteId.Shells,
        PickupKind.Nails => SpriteId.Nails,
        PickupKind.Rockets => SpriteId.Rockets,
        PickupKind.Cells => SpriteId.Cells,
        PickupKind.Slugs => SpriteId.Slugs,
        _ => weapon switch
        {
            WeaponId.Shotgun => SpriteId.Shotgun,
            WeaponId.SuperShotgun => SpriteId.SuperShotgun,
            WeaponId.Nailgun => SpriteId.Nailgun,
            WeaponId.SuperNailgun => SpriteId.SuperNailgun,
            WeaponId.GrenadeLauncher => SpriteId.GrenadeLauncher,
            WeaponId.RocketLauncher => SpriteId.RocketLauncher,
            WeaponId.LightningGun => SpriteId.LightningGun,
            WeaponId.Railgun => SpriteId.Railgun,
            _ => SpriteId.Axe,
        },
    };

    public static SpriteId For(Pickup p) => For(p.Kind, p.Weapon);

    public static bool IsWeapon(SpriteId s) => s >= SpriteId.Shotgun;

    /// <summary>Glow colour (0..1 rgb) used for the pickup's light and its floor pad.</summary>
    public static (float R, float G, float B) Glow(SpriteId s) => s switch
    {
        SpriteId.Health => (0.2f, 0.9f, 0.35f),
        SpriteId.Shells => (0.9f, 0.5f, 0.12f),
        SpriteId.Nails => (0.6f, 0.6f, 0.7f),
        SpriteId.Rockets => (0.9f, 0.2f, 0.15f),
        SpriteId.Cells => (0.3f, 0.6f, 1f),
        SpriteId.Slugs => (0.4f, 1f, 0.9f),
        _ => (1f, 0.85f, 0.2f),
    };
}
