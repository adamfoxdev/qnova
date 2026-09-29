namespace Qnova.Core;

public enum SoundId { Axe, Shotgun, SuperShotgun, Nailgun, SuperNailgun, GrenadeLaunch, RocketLaunch, Explosion, Bounce, DryFire, Pickup, PickupWeapon, PickupHealth, ItemRespawn, MenuMove, MenuSelect }

/// <summary>Procedurally synthesized 16-bit mono sound effects, so the game ships no audio assets.</summary>
public static class SoundSynth
{
    public const int SampleRate = 22050;

    public static SoundId ForWeapon(WeaponId w) => w switch
    {
        WeaponId.Axe => SoundId.Axe,
        WeaponId.Shotgun => SoundId.Shotgun,
        WeaponId.SuperShotgun => SoundId.SuperShotgun,
        WeaponId.Nailgun => SoundId.Nailgun,
        WeaponId.SuperNailgun => SoundId.SuperNailgun,
        WeaponId.GrenadeLauncher => SoundId.GrenadeLaunch,
        _ => SoundId.RocketLaunch,
    };

    public static SoundId ForPickup(PickupKind k) => k switch
    {
        PickupKind.Weapon => SoundId.PickupWeapon,
        PickupKind.Health => SoundId.PickupHealth,
        _ => SoundId.Pickup,
    };

    public static short[] Generate(SoundId id)
    {
        var rng = new Random(1234 + (int)id);   // deterministic
        return id switch
        {
            SoundId.Axe => Mix(Noise(rng, 0.18f, 0.06f, lowpass: 0.35f, attack: 0.05f, gain: 0.5f)),
            SoundId.Shotgun => Mix(
                Noise(rng, 0.35f, 0.09f, lowpass: 0.5f, gain: 0.9f),
                Tone(0.25f, 120, 45, decay: 0.07f, gain: 0.7f)),
            SoundId.SuperShotgun => Mix(
                Noise(rng, 0.5f, 0.13f, lowpass: 0.6f, gain: 1.0f),
                Tone(0.4f, 90, 30, decay: 0.12f, gain: 0.9f),
                Delay(Noise(rng, 0.3f, 0.07f, lowpass: 0.4f, gain: 0.6f), 0.06f)),
            SoundId.Nailgun => Mix(Tone(0.08f, 1400, 500, decay: 0.02f, gain: 0.5f, square: true), Noise(rng, 0.08f, 0.015f, lowpass: 0.9f, gain: 0.4f)),
            SoundId.SuperNailgun => Mix(Tone(0.09f, 1000, 350, decay: 0.025f, gain: 0.6f, square: true), Noise(rng, 0.09f, 0.02f, lowpass: 0.8f, gain: 0.5f)),
            SoundId.GrenadeLaunch => Mix(Tone(0.3f, 220, 60, decay: 0.1f, gain: 0.9f), Noise(rng, 0.2f, 0.05f, lowpass: 0.3f, gain: 0.5f)),
            SoundId.RocketLaunch => Mix(Noise(rng, 0.6f, 0.3f, lowpass: 0.12f, attack: 0.08f, gain: 0.9f), Tone(0.4f, 180, 70, decay: 0.15f, gain: 0.6f)),
            SoundId.Explosion => Mix(
                Noise(rng, 1.2f, 0.4f, lowpass: 0.08f, gain: 1.0f),
                Tone(1.0f, 70, 25, decay: 0.35f, gain: 1.0f),
                Noise(rng, 0.25f, 0.05f, lowpass: 0.6f, gain: 0.7f)),
            SoundId.Bounce => Mix(Tone(0.08f, 700, 300, decay: 0.03f, gain: 0.5f), Noise(rng, 0.05f, 0.01f, lowpass: 0.7f, gain: 0.4f)),
            SoundId.Pickup => Mix(Tone(0.2f, 900, 900, decay: 0.05f, gain: 0.5f), Delay(Tone(0.2f, 1350, 1350, decay: 0.06f, gain: 0.5f), 0.06f)),
            SoundId.PickupWeapon => Mix(
                Tone(0.5f, 520, 520, decay: 0.12f, gain: 0.5f),
                Delay(Tone(0.5f, 660, 660, decay: 0.12f, gain: 0.5f), 0.07f),
                Delay(Tone(0.5f, 880, 880, decay: 0.15f, gain: 0.5f), 0.14f)),
            SoundId.PickupHealth => Mix(Tone(0.35f, 660, 660, decay: 0.1f, gain: 0.5f), Delay(Tone(0.35f, 880, 880, decay: 0.1f, gain: 0.5f), 0.09f)),
            SoundId.ItemRespawn => Mix(Tone(0.3f, 300, 600, decay: 0.09f, gain: 0.45f)),
            SoundId.MenuMove => Mix(Tone(0.06f, 520, 380, decay: 0.02f, gain: 0.45f, square: true)),
            SoundId.MenuSelect => Mix(Tone(0.25f, 160, 60, decay: 0.08f, gain: 0.9f), Noise(rng, 0.12f, 0.03f, lowpass: 0.5f, gain: 0.5f)),
            _ => Mix(Tone(0.05f, 900, 600, decay: 0.012f, gain: 0.5f, square: true)),
        };
    }

    /// <summary>Wrap PCM in a RIFF/WAVE container (44-byte header) for loading from memory.</summary>
    public static byte[] ToWav(short[] pcm)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataLen = pcm.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataLen);
        foreach (var s in pcm) w.Write(s);
        return ms.ToArray();
    }

    // Each generator returns float samples in -1..1; Mix sums, normalizes peaks above 1, converts to PCM.

    static float[] Noise(Random rng, float seconds, float decay, float lowpass, float attack = 0.002f, float gain = 1f)
    {
        var o = new float[(int)(seconds * SampleRate)];
        float lp = 0;
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Exp(-t / decay) * MathF.Min(1f, t / attack);
            lp += lowpass * ((float)(rng.NextDouble() * 2 - 1) - lp);
            o[i] = lp * env * gain * 2f;
        }
        return o;
    }

    static float[] Tone(float seconds, float f0, float f1, float decay, float gain, bool square = false)
    {
        var o = new float[(int)(seconds * SampleRate)];
        double phase = 0;
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)SampleRate;
            float f = f0 + (f1 - f0) * MathF.Min(1f, t / seconds);
            phase += 2 * Math.PI * f / SampleRate;
            float s = (float)Math.Sin(phase);
            if (square) s = s >= 0 ? 0.6f : -0.6f;
            o[i] = s * MathF.Exp(-t / decay) * gain;
        }
        return o;
    }

    static float[] Delay(float[] x, float seconds)
    {
        int d = (int)(seconds * SampleRate);
        var o = new float[x.Length + d];
        Array.Copy(x, 0, o, d, x.Length);
        return o;
    }

    static short[] Mix(params float[][] layers)
    {
        int n = layers.Max(l => l.Length);
        var m = new float[n];
        foreach (var l in layers) for (int i = 0; i < l.Length; i++) m[i] += l[i];
        float peak = m.Max(Math.Abs);
        float k = peak > 0.95f ? 0.95f / peak : 1f;
        var pcm = new short[n];
        for (int i = 0; i < n; i++)
        {
            // short fade-out so nothing ends with a click
            float fade = Math.Min(1f, (n - i) / 64f);
            pcm[i] = (short)(m[i] * k * fade * short.MaxValue);
        }
        return pcm;
    }
}
