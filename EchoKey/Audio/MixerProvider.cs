using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;

namespace EchoKey.Audio;

/// <summary>One playing instance of a sound. Many voices can share the same sample data (overlap / spam).</summary>
public sealed class Voice
{
    readonly short[] _data;
    readonly float _gain;
    int _pos;
    int _fadeLeft = -1;
    int _fadeTotal;

    public Guid SoundId { get; }
    public bool Finished { get; private set; }
    public bool Fading => _fadeLeft >= 0;

    public Voice(Guid soundId, short[] data, float gain)
    {
        SoundId = soundId;
        _data = data;
        _gain = gain;
    }

    /// <summary>Start a short fade-out (avoids a click when stopping).</summary>
    public void BeginFade(int samples)
    {
        if (_fadeLeft < 0)
        {
            _fadeTotal = Math.Max(1, samples);
            _fadeLeft = _fadeTotal;
        }
    }

    public void MixInto(float[] dst, int off, int count)
    {
        int n = Math.Min(count, _data.Length - _pos);
        if (n <= 0) { Finished = true; return; }

        float g = _gain * (1f / 32768f);

        if (_fadeLeft < 0)
        {
            for (int i = 0; i < n; i++)
                dst[off + i] += _data[_pos + i] * g;
            _pos += n;
            if (_pos >= _data.Length) Finished = true;
            return;
        }

        for (int i = 0; i < n; i++)
        {
            if (_fadeLeft <= 0) { Finished = true; return; }
            dst[off + i] += _data[_pos + i] * g * (_fadeLeft / (float)_fadeTotal);
            _fadeLeft--;
            _pos++;
        }
        if (_fadeLeft <= 0 || _pos >= _data.Length) Finished = true;
    }
}

/// <summary>
/// Microphone path (BufferedWaveProvider -> float -> stereo -> 48 kHz) with a small jitter cushion.
/// Capture and playback run on different clocks; without a cushion every capture-packet jitter becomes an
/// underrun (a zero-filled gap = crackle). We wait until <see cref="PrimeMs"/> are buffered, and if the buffer
/// grows too large (clock drift) we drop the excess in one go instead of letting latency pile up.
/// </summary>
public sealed class MicSource
{
    public const double PrimeMs = 40;
    public const double MaxMs = 160;
    public const double TargetMs = 70;

    readonly ISampleProvider _source;
    readonly BufferedWaveProvider _buffer;
    readonly byte[] _scratch = new byte[8192];
    bool _primed;

    public MicSource(ISampleProvider source, BufferedWaveProvider buffer)
    {
        _source = source;
        _buffer = buffer;
    }

    public int Read(float[] dst, int offset, int count)
    {
        double ms = _buffer.BufferedDuration.TotalMilliseconds;

        if (!_primed)
        {
            if (ms < PrimeMs)
            {
                Array.Clear(dst, offset, count);
                return count;
            }
            _primed = true;
        }
        else if (_buffer.BufferedBytes == 0)
        {
            _primed = false; // real underrun: rebuild the cushion instead of stuttering on every packet
            Array.Clear(dst, offset, count);
            return count;
        }

        if (ms > MaxMs) DropExcess(ms);

        return _source.Read(dst, offset, count);
    }

    void DropExcess(double ms)
    {
        int align = Math.Max(1, _buffer.WaveFormat.BlockAlign);
        int bytesPerMs = _buffer.WaveFormat.AverageBytesPerSecond / 1000;
        int excess = (int)((ms - TargetMs) * bytesPerMs);
        excess -= excess % align;
        while (excess > 0)
        {
            int n = Math.Min(excess, _scratch.Length - (_scratch.Length % align));
            int read = _buffer.Read(_scratch, 0, n);
            if (read <= 0) break;
            excess -= read;
        }
    }
}

static class AudioThread
{
    [ThreadStatic] static bool _boosted;

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

    /// <summary>Registers the calling (audio callback) thread with MMCSS once, so a busy PC does not starve it.</summary>
    public static void Boost()
    {
        if (_boosted) return;
        _boosted = true;
        try { uint idx = 0; AvSetMmThreadCharacteristicsW("Pro Audio", ref idx); } catch { }
    }
}

/// <summary>
/// Real-time mixer. Output: 48 kHz, stereo, 32-bit float.
///   out = limiter( (mic * micGain + voices * soundboardGain) * gate )
/// All gains are smoothed per sample, so volume changes, mute/unmute, device restarts and Stop-all never produce
/// clicks. The gate starts closed: the pipe opens it after the device is running and closes it (fade to silence)
/// before the device is stopped, so the driver never repeats a non-silent buffer (the "stuck buzz").
/// The same class is used for the virtual-mic channel (with a microphone) and for the headphones channel (never given one).
/// </summary>
public sealed class MixerProvider : ISampleProvider
{
    public const int SampleRate = 48000;
    public const int MaxVoices = 512; // safety cap only; far above what anyone can trigger by hand
    const int FadeSamples = 48 * 2 * 8; // ~8 ms per-voice fade on Stop all
    const float GateStep = 1f / 1440f;  // 30 ms full-scale ramp (per frame)
    const float GainStep = 1f / 960f;   // 20 ms full-scale ramp (per frame)
    const float LimiterKnee = 0.9f;

    readonly object _lock = new();
    readonly List<Voice> _voices = new(64);
    float[] _voiceBuf = new float[16384];

    volatile MicSource? _mic;
    volatile float _micVolume = 1f;
    volatile float _soundboardVolume = 1f;
    volatile bool _micEnabled = true;
    volatile bool _micSuspended;
    volatile bool _gateOpen;

    float _micGain, _sbGain, _gateGain; // smoothed values (audio thread only)

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    public float MicVolume { get => _micVolume; set => _micVolume = value; }
    public float SoundboardVolume { get => _soundboardVolume; set => _soundboardVolume = value; }
    public bool MicEnabled { get => _micEnabled; set => _micEnabled = value; }
    /// <summary>Temporarily silences the mic (smoothly) while its device is swapped.</summary>
    public bool MicSuspended { get => _micSuspended; set => _micSuspended = value; }
    /// <summary>false = output fades to silence; true = fades in. Starts closed.</summary>
    public bool GateOpen { get => _gateOpen; set => _gateOpen = value; }
    public bool HasMic => _mic != null;

    public void SetMic(MicSource? mic) => _mic = mic;

    public int ActiveCount
    {
        get { lock (_lock) return _voices.Count; }
    }

    public void Play(Guid soundId, short[] data, float gain)
    {
        if (data.Length == 0) return;
        lock (_lock)
        {
            if (_voices.Count >= MaxVoices) return;
            _voices.Add(new Voice(soundId, data, gain));
        }
    }

    /// <summary>Fade out and remove every voice.</summary>
    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var v in _voices) v.BeginFade(FadeSamples);
        }
    }

    /// <summary>Remove every voice immediately (only used while the gate is closed).</summary>
    public void Clear()
    {
        lock (_lock) _voices.Clear();
    }

    /// <summary>Fills <paramref name="counts"/> with number of active (non-fading) voices per sound.</summary>
    public int Snapshot(Dictionary<Guid, int> counts)
    {
        counts.Clear();
        int total = 0;
        lock (_lock)
        {
            foreach (var v in _voices)
            {
                if (v.Fading) continue;
                total++;
                counts.TryGetValue(v.SoundId, out int c);
                counts[v.SoundId] = c + 1;
            }
        }
        return total;
    }

    static float Approach(float cur, float target, float step)
    {
        if (cur < target) { cur += step; if (cur > target) cur = target; }
        else if (cur > target) { cur -= step; if (cur < target) cur = target; }
        return cur;
    }

    /// <summary>Transparent below 0.9, smooth tanh knee above it: overlapping loud sounds never hard-clip.</summary>
    static float Limit(float x)
    {
        if (float.IsNaN(x)) return 0f;
        float a = MathF.Abs(x);
        if (a <= LimiterKnee) return x;
        float y = LimiterKnee + (1f - LimiterKnee) * MathF.Tanh((a - LimiterKnee) / (1f - LimiterKnee));
        return x < 0 ? -y : y;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        AudioThread.Boost();
        count &= ~1; // whole stereo frames

        // 1) sum the voices into a private buffer
        if (_voiceBuf.Length < count) _voiceBuf = new float[count];
        Array.Clear(_voiceBuf, 0, count);
        lock (_lock)
        {
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                var v = _voices[i];
                v.MixInto(_voiceBuf, 0, count);
                if (v.Finished)
                {
                    int last = _voices.Count - 1;
                    _voices[i] = _voices[last];
                    _voices.RemoveAt(last);
                }
            }
        }

        // 2) microphone (always drained so its buffer never backs up, even when muted)
        var mic = _mic;
        if (mic != null)
        {
            int n = mic.Read(buffer, offset, count);
            if (n < count) Array.Clear(buffer, offset + n, count - n);
        }
        else
        {
            Array.Clear(buffer, offset, count);
        }

        // 3) combine with per-sample smoothing, gate and limiter
        float micTarget = (_micEnabled && !_micSuspended) ? _micVolume : 0f;
        float sbTarget = _soundboardVolume;
        float gateTarget = _gateOpen ? 1f : 0f;
        float mg = _micGain, sg = _sbGain, gg = _gateGain;

        for (int i = 0; i < count; i += 2)
        {
            mg = Approach(mg, micTarget, GainStep);
            sg = Approach(sg, sbTarget, GainStep);
            gg = Approach(gg, gateTarget, GateStep);
            int o = offset + i;
            buffer[o] = Limit((buffer[o] * mg + _voiceBuf[i] * sg) * gg);
            buffer[o + 1] = Limit((buffer[o + 1] * mg + _voiceBuf[i + 1] * sg) * gg);
        }

        _micGain = mg;
        _sbGain = sg;
        _gateGain = gg;
        return count;
    }
}
