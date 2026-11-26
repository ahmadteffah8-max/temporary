using System;
using System.Collections.Generic;
using System.Linq;
using EchoKey.Audio;
using NAudio.Wave;
using Xunit;

namespace EchoKey.Tests;

public class MixerTests
{
    static short[] Const(short v, int frames)
    {
        var a = new short[frames * 2];
        Array.Fill(a, v);
        return a;
    }

    static float[] ReadN(MixerProvider m, int samples)
    {
        var b = new float[samples];
        int got = 0;
        while (got < samples) got += m.Read(b, got, Math.Min(960, samples - got));
        return b;
    }

    /// <summary>A mixer whose gate is open and whose smoothed gains have settled.</summary>
    static MixerProvider Open()
    {
        var m = new MixerProvider { GateOpen = true };
        ReadN(m, 9600);
        return m;
    }

    [Fact]
    public void Overlap_SumsVoicesOfTheSameSound()
    {
        var m = Open();
        var id = Guid.NewGuid();
        var data = Const(8192, 48000); // 0.25
        for (int i = 0; i < 3; i++) m.Play(id, data, 1f);

        var o = ReadN(m, 960);
        Assert.Equal(3, m.ActiveCount);
        Assert.InRange(o[500], 0.749f, 0.751f);
    }

    [Fact]
    public void Spam_HundredInstancesAreAllKept()
    {
        var m = Open();
        var id = Guid.NewGuid();
        var data = Const(100, 48000);
        for (int i = 0; i < 100; i++) m.Play(id, data, 1f);

        var counts = new Dictionary<Guid, int>();
        Assert.Equal(100, m.Snapshot(counts));
        Assert.Equal(100, counts[id]);
    }

    [Fact]
    public void DifferentSoundsPlayAtTheSameTime()
    {
        var m = Open();
        m.Play(Guid.NewGuid(), Const(4096, 48000), 1f);  // 0.125
        m.Play(Guid.NewGuid(), Const(8192, 48000), 1f);  // 0.25
        var o = ReadN(m, 960);
        Assert.InRange(o[300], 0.374f, 0.376f);
    }

    [Fact]
    public void StopAll_FadesWithoutClickAndRemovesEveryVoice()
    {
        var m = Open();
        for (int i = 0; i < 5; i++) m.Play(Guid.NewGuid(), Const(8192, 480000), 1f);
        m.StopAll();
        var o = ReadN(m, 4800);

        Assert.Equal(0, m.ActiveCount);
        Assert.Equal(0f, o[^1]);
        float maxStep = 0;
        for (int i = 2; i < o.Length; i++) maxStep = Math.Max(maxStep, Math.Abs(o[i] - o[i - 2]));
        Assert.True(maxStep < 0.01f, "Stop all must fade, not cut (max step " + maxStep + ")");
    }

    [Fact]
    public void Limiter_NeverClipsOrProducesNaN()
    {
        var m = Open();
        for (int i = 0; i < 50; i++) m.Play(Guid.NewGuid(), Const(32767, 48000), 2f);
        var o = ReadN(m, 4800);
        Assert.All(o, x => Assert.True(!float.IsNaN(x) && Math.Abs(x) <= 1.0f));
        Assert.True(o.Max() > 0.99f);
    }

    [Fact]
    public void Limiter_IsTransparentBelowTheKnee()
    {
        var m = Open();
        m.Play(Guid.NewGuid(), Const(16384, 48000), 1f); // 0.5
        Assert.Equal(0.5f, ReadN(m, 960)[100], 4);
    }

    [Fact]
    public void Gate_StartsSilent_FadesIn_AndFadesOutToExactSilence()
    {
        var m = new MixerProvider();
        m.Play(Guid.NewGuid(), Const(8192, 480000), 1f);

        var closed = ReadN(m, 960);
        Assert.All(closed, x => Assert.Equal(0f, x));

        m.GateOpen = true;
        var rising = ReadN(m, 3840);
        Assert.True(rising[0] < 0.01f, "must not start with a click");
        for (int i = 2; i < rising.Length; i++) Assert.True(rising[i] >= rising[i - 2] - 1e-6f);

        m.GateOpen = false;
        var falling = ReadN(m, 4800);
        Assert.Equal(0f, falling[^1]);
        Assert.Equal(0f, falling[^2]);
    }

    [Fact]
    public void VolumeChanges_AreSmoothed()
    {
        var m = Open();
        m.Play(Guid.NewGuid(), Const(16384, 480000), 1f); // 0.5
        ReadN(m, 960);
        m.SoundboardVolume = 0f;
        var o = ReadN(m, 4800);

        float maxStep = 0;
        for (int i = 2; i < o.Length; i++) maxStep = Math.Max(maxStep, Math.Abs(o[i] - o[i - 2]));
        Assert.True(maxStep < 0.002f, "volume jump " + maxStep);
        Assert.Equal(0f, o[^1]);
    }

    // ---------------- two independent channels ----------------

    static (BufferedWaveProvider buf, MicSource mic) NewMic()
    {
        var buf = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            ReadFully = true
        };
        return (buf, new MicSource(buf.ToSampleProvider(), buf));
    }

    static void Feed(BufferedWaveProvider buf, int ms, float value)
    {
        var f = new float[48 * 2 * ms];
        Array.Fill(f, value);
        var bytes = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, bytes, 0, bytes.Length);
        buf.AddSamples(bytes, 0, bytes.Length);
    }

    [Fact]
    public void VirtualMicChannel_MixesMicrophoneAndSounds_SpeakersChannelNeverHearsTheMic()
    {
        var main = new MixerProvider { GateOpen = true };       // channel 1: -> VB-CABLE
        var speakers = new MixerProvider { GateOpen = true };   // channel 2: -> headphones (no microphone, ever)

        var (buf, mic) = NewMic();
        main.SetMic(mic);
        Feed(buf, 300, 0.3f);
        ReadN(main, 4800);
        ReadN(speakers, 4800);

        var id = Guid.NewGuid();
        var data = Const(8192, 48000); // 0.25
        main.Play(id, data, 1f);
        speakers.Play(id, data, 1f);

        Feed(buf, 300, 0.3f);
        var a = ReadN(main, 960);
        var b = ReadN(speakers, 960);

        Assert.InRange(a[400], 0.549f, 0.551f);   // mic 0.3 + sound 0.25
        Assert.InRange(b[400], 0.249f, 0.251f);   // sound only
        Assert.False(speakers.HasMic);
    }

    [Fact]
    public void MutingTheMic_DoesNotAffectSounds()
    {
        var main = new MixerProvider { GateOpen = true };
        var (buf, mic) = NewMic();
        main.SetMic(mic);
        Feed(buf, 300, 0.3f);
        ReadN(main, 4800);

        main.MicEnabled = false;
        main.Play(Guid.NewGuid(), Const(8192, 48000), 1f);
        Feed(buf, 400, 0.3f);
        ReadN(main, 4800);   // let the mute ramp finish
        Feed(buf, 300, 0.3f);
        var o = ReadN(main, 960);
        Assert.InRange(o[300], 0.249f, 0.251f);
    }

    [Fact]
    public void MicSource_WaitsForACushion_AndDoesNotConsumeWhileEmpty()
    {
        var (buf, mic) = NewMic();
        var o = new float[960];
        mic.Read(o, 0, 960);
        Assert.All(o, x => Assert.Equal(0f, x));

        Feed(buf, 10, 0.5f);               // less than the 40 ms cushion: still silent
        mic.Read(o, 0, 960);
        Assert.All(o, x => Assert.Equal(0f, x));
        Assert.True(buf.BufferedDuration.TotalMilliseconds >= 9);

        Feed(buf, 100, 0.5f);              // cushion reached
        mic.Read(o, 0, 960);
        Assert.Equal(0.5f, o[10], 3);
    }

    [Fact]
    public void MicSource_DropsExcessLatencyCaused_ByClockDrift()
    {
        var (buf, mic) = NewMic();
        Feed(buf, 1000, 0.2f);
        var o = new float[960];
        mic.Read(o, 0, 960);
        Assert.True(buf.BufferedDuration.TotalMilliseconds < MicSource.MaxMs);
    }

    [Fact]
    public void MicSuspended_SilencesTheMicSmoothly()
    {
        var main = new MixerProvider { GateOpen = true };
        var (buf, mic) = NewMic();
        main.SetMic(mic);
        Feed(buf, 300, 0.3f);
        ReadN(main, 4800);

        main.MicSuspended = true;
        Feed(buf, 300, 0.3f);
        var o = ReadN(main, 960);
        float maxStep = 0;
        for (int i = 2; i < o.Length; i++) maxStep = Math.Max(maxStep, Math.Abs(o[i] - o[i - 2]));
        Assert.True(maxStep < 0.002f);
    }
}
