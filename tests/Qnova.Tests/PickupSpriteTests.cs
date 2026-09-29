using Qnova.Core;

namespace Qnova.Tests;

public class PickupSpriteTests
{
    [Fact]
    public void Every_weapon_pickup_maps_to_its_own_weapon_sprite()
    {
        var seen = new HashSet<SpriteId>();
        foreach (WeaponId w in Enum.GetValues<WeaponId>())
        {
            var s = PickupSprites.For(PickupKind.Weapon, w);
            Assert.True(PickupSprites.IsWeapon(s));
            Assert.True(seen.Add(s), $"{w} shares a sprite");
        }
        Assert.Equal(SpriteId.Railgun, PickupSprites.For(PickupKind.Weapon, WeaponId.Railgun));
    }

    [Fact]
    public void Items_and_ammo_have_distinct_non_weapon_sprites()
    {
        var kinds = new[] { PickupKind.Health, PickupKind.Shells, PickupKind.Nails, PickupKind.Rockets, PickupKind.Cells, PickupKind.Slugs };
        var sprites = kinds.Select(k => PickupSprites.For(k, WeaponId.Axe)).ToList();
        Assert.Equal(kinds.Length, sprites.Distinct().Count());
        Assert.All(sprites, s => Assert.False(PickupSprites.IsWeapon(s)));
    }

    [Fact]
    public void Pickup_overload_and_glow_are_valid()
    {
        var p = new Pickup { Kind = PickupKind.Weapon, Weapon = WeaponId.LightningGun };
        Assert.Equal(SpriteId.LightningGun, PickupSprites.For(p));
        foreach (SpriteId s in Enum.GetValues<SpriteId>())
        {
            var (r, g, b) = PickupSprites.Glow(s);
            Assert.InRange(r, 0f, 1f); Assert.InRange(g, 0f, 1f); Assert.InRange(b, 0f, 1f);
        }
    }
}
