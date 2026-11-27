using System;
using System.Collections.Generic;
using Godot;

namespace StreamRaid;

/// <summary>
/// 効果音。音声ファイルは使わず、起動時にコードで波形を作る(ライセンスの心配がなく、軽い)。
/// 番人の予兆は種類ごとに違う音にして、耳でも見分けられるようにする。
/// </summary>
public static class Sfx
{
    const int Rate = 22050;
    static readonly Dictionary<string, AudioStreamWav> Sounds = new();
    static readonly List<AudioStreamPlayer> Players = new();
    static int _next;

    public static void Init(Node owner)
    {
        Sounds["tell_right"] = Make(0.45f, t => Sweep(t, 0.45f, 280, 820) * Env(t, 0.45f) * 0.55f + Noise() * Env(t, 0.45f) * 0.12f);
        Sounds["tell_left"] = Make(0.45f, t => Sweep(t, 0.45f, 820, 280) * Env(t, 0.45f) * 0.55f + Noise() * Env(t, 0.45f) * 0.12f);
        Sounds["tell_nova"] = Make(0.7f, t => (Mathf.Sin(Mathf.Tau * 70 * t) * 0.6f + Noise() * 0.35f) * Env(t, 0.7f) * (0.4f + t));
        Sounds["tell_target"] = Make(0.42f, t => Mathf.Sin(Mathf.Tau * 1320 * t) * (t < 0.12f ? Env(t, 0.12f) : t > 0.2f && t < 0.32f ? Env(t - 0.2f, 0.12f) : 0) * 0.5f);
        Sounds["tell_volley"] = Make(0.3f, t => Sweep(t, 0.3f, 600, 1400) * Env(t, 0.3f) * 0.35f);
        Sounds["impact"] = Make(0.35f, t => (Noise() * 0.7f + Mathf.Sin(Mathf.Tau * 55 * t) * 0.6f) * MathF.Exp(-t * 11) * 0.8f);
        Sounds["slash"] = Make(0.14f, t => Noise() * Env(t, 0.14f) * 0.35f);
        Sounds["hit"] = Make(0.08f, t => (Noise() * 0.5f + Mathf.Sin(Mathf.Tau * 180 * t) * 0.4f) * MathF.Exp(-t * 40) * 0.6f);
        Sounds["roar"] = Make(0.9f, t => (Saw(t * (90 - t * 40)) * 0.5f + Noise() * 0.25f) * Env(t, 0.9f) * 0.7f);
        Sounds["clear"] = Make(0.6f, t => (Mathf.Sin(Mathf.Tau * (t < 0.15f ? 660 : t < 0.3f ? 880 : 1320) * t) * 0.4f) * Env(t, 0.6f));
        for (int i = 0; i < 10; i++)
        {
            var p = new AudioStreamPlayer();
            owner.AddChild(p);
            Players.Add(p);
        }
    }

    /// <summary>0〜100(設定の「効果音の音量」)。</summary>
    public static int Volume = 70;

    public static void Play(string name, float pitch = 1f)
    {
        if (Volume <= 0 || !Sounds.TryGetValue(name, out var s) || Players.Count == 0) return;
        var p = Players[_next++ % Players.Count];
        p.Stream = s;
        p.PitchScale = pitch;
        p.VolumeDb = Mathf.LinearToDb(Volume / 100f);
        p.Play();
    }

    static AudioStreamWav Make(float seconds, Func<float, float> wave)
    {
        int n = (int)(seconds * Rate);
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float v = Mathf.Clamp(wave(i / (float)Rate), -1, 1);
            short s = (short)(v * 30000);
            data[i * 2] = (byte)(s & 0xff);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }

    static readonly Random Rng = new(1);
    static float Noise() => (float)(Rng.NextDouble() * 2 - 1);
    static float Saw(float phase) => 2 * (phase - Mathf.Floor(phase + 0.5f));
    /// <summary>周波数を a → b に変えるサイン波(ヒュンという音)。</summary>
    static float Sweep(float t, float len, float a, float b) => Mathf.Sin(Mathf.Tau * (a * t + (b - a) * t * t / (2 * len)));
    /// <summary>立ち上がり 10%、あとはなだらかに消える音量。</summary>
    static float Env(float t, float len) => t < len * 0.1f ? t / (len * 0.1f) : Mathf.Max(0, 1 - (t - len * 0.1f) / (len * 0.9f));
}
