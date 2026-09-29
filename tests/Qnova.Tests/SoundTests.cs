using System.Numerics;
using Qnova.Core;

namespace Qnova.Tests;

public class SoundTests
{
    [Fact]
    public void Every_sound_is_generated_audible_unclipped_and_deterministic()
    {
        foreach (var id in Enum.GetValues<SoundId>())
        {
            var a = SoundSynth.Generate(id);
            var b = SoundSynth.Generate(id);
            Assert.Equal(a, b);
            Assert.True(a.Length > SoundSynth.SampleRate / 50, $"{id} too short");
            int peak = a.Max(x => Math.Abs((int)x));
            Assert.InRange(peak, 3000, short.MaxValue - 1000);
            Assert.True(Math.Abs(a[^1]) < 500, $"{id} ends with a click");
        }
    }

    [Fact]
    public void Explosion_is_longer_than_gunshots()
    {
        Assert.True(SoundSynth.Generate(SoundId.Explosion).Length > SoundSynth.Generate(SoundId.Shotgun).Length * 2);
    }

    [Fact]
    public void Wav_container_has_valid_header()
    {
        var pcm = SoundSynth.Generate(SoundId.Bounce);
        var wav = SoundSynth.ToWav(pcm);
        Assert.Equal(44 + pcm.Length * 2, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(SoundSynth.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(pcm.Length * 2, BitConverter.ToInt32(wav, 40));
    }

    [Theory]
    [InlineData(WeaponId.Axe, SoundId.Axe)]
    [InlineData(WeaponId.Shotgun, SoundId.Shotgun)]
    [InlineData(WeaponId.SuperShotgun, SoundId.SuperShotgun)]
    [InlineData(WeaponId.Nailgun, SoundId.Nailgun)]
    [InlineData(WeaponId.SuperNailgun, SoundId.SuperNailgun)]
    [InlineData(WeaponId.GrenadeLauncher, SoundId.GrenadeLaunch)]
    [InlineData(WeaponId.RocketLauncher, SoundId.RocketLaunch)]
    public void Weapon_maps_to_its_sound(WeaponId w, SoundId s) => Assert.Equal(s, SoundSynth.ForWeapon(w));

    static GameWorld Flat()
    {
        var w = new World();
        w.Add(new(-10000, -64, -10000), new(10000, 0, 10000));
        var g = new GameWorld(w, new Vector3(0, 28, 0));
        g.Tick(default, false);
        return g;
    }

    [Fact]
    public void Firing_emits_one_shot_event_with_the_weapon()
    {
        var g = Flat();
        g.Player.Current = WeaponId.SuperShotgun;
        g.Tick(default, true);
        var shot = Assert.Single(g.Events, e => e.Kind == EventKind.Shot);
        Assert.Equal((int)WeaponId.SuperShotgun, shot.Arg);
    }

    [Fact]
    public void Empty_weapon_emits_rate_limited_dry_fire_and_no_shot()
    {
        var g = Flat();
        g.Player.Current = WeaponId.Nailgun; g.Player.Nails = 0;
        for (int i = 0; i < (int)(GameWorld.TickRate * 1.0f); i++) g.Tick(default, true);
        Assert.DoesNotContain(g.Events, e => e.Kind == EventKind.Shot);
        int clicks = g.Events.Count(e => e.Kind == EventKind.DryFire);
        Assert.InRange(clicks, 3, 5);   // 0.25s spacing over 1s
    }

    [Fact]
    public void Grenade_bounce_emits_bounce_event()
    {
        var g = Flat();
        g.Player.Current = WeaponId.GrenadeLauncher;
        g.Tick(default, true);
        for (int i = 0; i < (int)(GameWorld.TickRate * 1.5f); i++) g.Tick(default, false);
        Assert.Contains(g.Events, e => e.Kind == EventKind.Bounce);
    }
}
