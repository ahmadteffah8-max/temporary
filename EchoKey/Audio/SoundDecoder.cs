using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EchoKey.Core;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EchoKey.Audio;

public static class SoundDecoder
{
    /// <summary>Longest clip accepted. Clips are kept decoded in RAM (16-bit, 48 kHz stereo ≈ 11.5 MB/min).</summary>
    public const int MaxSeconds = 300;

    public static readonly string[] Extensions = { ".mp3", ".wav", ".ogg", ".oga", ".flac" };

    public static bool IsSupported(string path) =>
        Array.IndexOf(Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

    static WaveStream Open(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".ogg" || ext == ".oga")
            return new VorbisWaveReader(path);          // NAudio.Vorbis (NVorbis)
        return new AudioFileReader(path);               // WAV, MP3 and FLAC (via Windows Media Foundation)
    }

    /// <summary>Opens the file just to validate it and read its duration.</summary>
    public static double GetDuration(string path)
    {
        using var reader = Open(path);
        return reader.TotalTime.TotalSeconds;
    }

    /// <summary>Decodes to interleaved 16-bit PCM, 48 kHz, stereo.</summary>
    public static short[] Decode(string path)
    {
        using var reader = Open(path);
        ISampleProvider sp = (ISampleProvider)reader;

        int channels = sp.WaveFormat.Channels;
        if (channels == 1)
        {
            sp = new MonoToStereoSampleProvider(sp);
        }
        else if (channels > 2)
        {
            var mux = new MultiplexingSampleProvider(new[] { sp }, 2);
            mux.ConnectInputToOutput(0, 0);
            mux.ConnectInputToOutput(1, 1);
            sp = mux;
        }
        if (sp.WaveFormat.SampleRate != MixerProvider.SampleRate)
            sp = new WdlResamplingSampleProvider(sp, MixerProvider.SampleRate);

        int maxSamples = MaxSeconds * MixerProvider.SampleRate * 2;
        long guess = (long)(reader.TotalTime.TotalSeconds * MixerProvider.SampleRate * 2) + 96000;
        var result = new short[(int)Math.Min(maxSamples, Math.Max(96000, guess))];
        int total = 0;
        var buf = new float[9600];

        while (true)
        {
            int n = sp.Read(buf, 0, buf.Length);
            if (n <= 0) break;
            if (total + n > result.Length)
            {
                if (result.Length >= maxSamples) break;
                Array.Resize(ref result, Math.Min(maxSamples, Math.Max(result.Length * 2, total + n)));
                if (total + n > result.Length) n = result.Length - total;
            }
            for (int i = 0; i < n; i++)
            {
                float f = buf[i];
                if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
                result[total + i] = (short)(f * 32767f);
            }
            total += n;
        }

        if ((total & 1) != 0) total--;
        if (total != result.Length) Array.Resize(ref result, total);
        return result;
    }
}
