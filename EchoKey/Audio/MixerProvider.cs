using System;
using System.Collections.Generic;
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

    public void MixInto(float[] dst, int off, int count, float master)
    {
        int n = Math.Min(count, _data.Length - _pos);
        if (n <= 0) { Finished = true; return; }

        float g = _gain * master * (1f / 32768f);

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

/// <summary>Microphone path: BufferedWaveProvider -> float -> stereo -> 48 kHz.</summary>
public sealed class MicSource
{
    readonly ISampleProvider _source;
    readonly BufferedWaveProvider _buffer;

    public MicSource(ISampleProvider source, BufferedWaveProvider buffer)
    {
        _source = source;
        _buffer = buffer;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        // Capture and playback clocks drift slightly; if latency piles up, drop the backlog.
        if (_buffer.BufferedDuration.TotalMilliseconds > 150)
            _buffer.ClearBuffer();
        return _source.Read(buffer, offset, count);
    }
}

/// <summary>
/// Real-time mixer: (real microphone * micVolume) + sum(voices * soundboardVolume).
/// Output format is fixed: 48 kHz, stereo, 32-bit float.
/// </summary>
public sealed class MixerProvider : ISampleProvider
{
    public const int SampleRate = 48000;
    public const int MaxVoices = 512; // safety cap only; far above what anyone can trigger by hand
    const int FadeSamples = 48 * 2 * 8; // ~8 ms

    readonly object _lock = new();
    readonly List<Voice> _voices = new(64);
    volatile MicSource? _mic;
    volatile float _micVolume = 1f;
    volatile float _soundboardVolume = 1f;
    volatile bool _micEnabled = true;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    public float MicVolume { get => _micVolume; set => _micVolume = value; }
    public float SoundboardVolume { get => _soundboardVolume; set => _soundboardVolume = value; }
    public bool MicEnabled { get => _micEnabled; set => _micEnabled = value; }

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

    /// <summary>Remove every voice immediately (used when the audio engine stops).</summary>
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

    public int Read(float[] buffer, int offset, int count)
    {
        // 1) real microphone
        var mic = _mic;
        float micGain = _micEnabled ? _micVolume : 0f;
        if (mic != null)
        {
            int n = mic.Read(buffer, offset, count); // always read so the buffer keeps draining while muted
            if (n < count) Array.Clear(buffer, offset + n, count - n);
            if (micGain == 0f)
                Array.Clear(buffer, offset, count);
            else if (micGain != 1f)
                for (int i = 0; i < count; i++) buffer[offset + i] *= micGain;
        }
        else
        {
            Array.Clear(buffer, offset, count);
        }

        // 2) soundboard voices
        float master = _soundboardVolume;
        lock (_lock)
        {
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                var v = _voices[i];
                v.MixInto(buffer, offset, count, master);
                if (v.Finished)
                {
                    int last = _voices.Count - 1;
                    _voices[i] = _voices[last];
                    _voices.RemoveAt(last);
                }
            }
        }

        // 3) hard limit to avoid wrap-around distortion when many sounds overlap
        for (int i = 0; i < count; i++)
        {
            float s = buffer[offset + i];
            if (s > 1f) buffer[offset + i] = 1f;
            else if (s < -1f) buffer[offset + i] = -1f;
        }

        return count;
    }
}
