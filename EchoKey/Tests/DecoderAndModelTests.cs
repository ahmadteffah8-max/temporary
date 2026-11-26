using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using EchoKey.Audio;
using EchoKey.Core;
using NAudio.Wave;
using Xunit;

namespace EchoKey.Tests;

public class DecoderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "echokey-tests-" + Guid.NewGuid().ToString("N"));

    public DecoderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    string MakeWav(int rate, int channels, double seconds, double amplitude = 0.6)
    {
        string path = Path.Combine(_dir, $"t_{rate}_{channels}.wav");
        using var w = new WaveFileWriter(path, new WaveFormat(rate, 16, channels));
        int frames = (int)(rate * seconds);
        for (int i = 0; i < frames; i++)
        {
            // constant (non-zero at both ends) so the edge fades are observable
            short v = (short)(amplitude * 32767);
            for (int c = 0; c < channels; c++) w.WriteSample(v / 32768f);
        }
        return path;
    }

    [Theory]
    [InlineData("a.mp3", true)]
    [InlineData("a.WAV", true)]
    [InlineData("a.ogg", true)]
    [InlineData("a.oga", true)]
    [InlineData("a.flac", true)]
    [InlineData("a.txt", false)]
    [InlineData("a", false)]
    public void IsSupported_CoversTheFiveFormats(string name, bool expected) =>
        Assert.Equal(expected, SoundDecoder.IsSupported(name));

    [Fact]
    public void Duration_IsReadFromTheFile()
    {
        string p = MakeWav(44100, 1, 0.5);
        Assert.InRange(SoundDecoder.GetDuration(p), 0.49, 0.51);
    }

    [Fact]
    public void Decode_ConvertsToStereo48k()
    {
        string p = MakeWav(44100, 1, 0.5);
        var pcm = SoundDecoder.Decode(p);
        Assert.Equal(0, pcm.Length % 2);
        Assert.InRange(pcm.Length / 2, 24000 - 480, 24000 + 480);      // ~0.5 s at 48 kHz
        for (int i = 0; i < pcm.Length; i += 2) Assert.Equal(pcm[i], pcm[i + 1]); // mono duplicated to both channels
        Assert.True(pcm.Max(x => (int)x) > 0.5 * 32767);
    }

    [Fact]
    public void Decode_48kStereo_KeepsExactLength()
    {
        string p = MakeWav(48000, 2, 0.25);
        var pcm = SoundDecoder.Decode(p);
        Assert.Equal(48000 / 4 * 2, pcm.Length);
    }

    [Fact]
    public void Decode_StartsAndEndsAtZero_SoNothingClicks()
    {
        string p = MakeWav(48000, 2, 0.25);
        var pcm = SoundDecoder.Decode(p);
        Assert.Equal(0, pcm[0]);
        Assert.Equal(0, pcm[1]);
        Assert.True(Math.Abs(pcm[^1]) < 300);
        Assert.True(Math.Abs(pcm[^2]) < 300);
        Assert.True(pcm[pcm.Length / 2] > 15000); // untouched in the middle
    }

    [Fact]
    public void Decode_RefusesNonAudio()
    {
        string p = Path.Combine(_dir, "broken.wav");
        File.WriteAllText(p, "this is not audio");
        Assert.ThrowsAny<Exception>(() => SoundDecoder.Decode(p));
    }
}

public class ModelTests
{
    [Fact]
    public void HotkeyBinding_FormatsAndComparesCombos()
    {
        var f1 = new HotkeyBinding { Modifiers = 0, VirtualKey = 0x70 };
        var ctrlF1 = new HotkeyBinding { Modifiers = HotkeyBinding.ModControl, VirtualKey = 0x70 };
        var altQ = new HotkeyBinding { Modifiers = HotkeyBinding.ModAlt, VirtualKey = 0x51 };

        Assert.Equal("F1", f1.ToString());
        Assert.Equal("Ctrl + F1", ctrlF1.ToString());
        Assert.Equal("Alt + Q", altQ.ToString());
        Assert.True(f1.SameAs(new HotkeyBinding { Modifiers = 0, VirtualKey = 0x70 }));
        Assert.False(f1.SameAs(ctrlF1));
        Assert.False(f1.SameAs(null));
    }

    [Fact]
    public void Settings_PersistTheStopAllHotkeyAndAudioChoices()
    {
        var s = new AppSettings
        {
            StopAllHotkey = new HotkeyBinding { Modifiers = HotkeyBinding.ModControl | HotkeyBinding.ModShift, VirtualKey = 0x78 },
            MonitorEnabled = false,
            MonitorDeviceId = "dev-1",
            MicDeviceId = "mic-1",
            OutputDeviceId = "out-1",
            OutputLatencyMs = 55
        };
        var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.True(s.StopAllHotkey.SameAs(back.StopAllHotkey));
        Assert.Equal("Ctrl + Shift + F9", back.StopAllHotkey!.ToString());
        Assert.False(back.MonitorEnabled);
        Assert.Equal("dev-1", back.MonitorDeviceId);
        Assert.Equal(55, back.OutputLatencyMs);
    }

    [Fact]
    public void Settings_Defaults_HearSoundsOnSpeakers_AndNoStopAllHotkey()
    {
        var s = new AppSettings();
        Assert.True(s.MonitorEnabled);
        Assert.Equal("default", s.MonitorDeviceId);
        Assert.Null(s.StopAllHotkey);
    }

    [Fact]
    public void Library_RoundTripsSoundsWithHotkeys()
    {
        var lib = new LibraryData();
        lib.Categories.Add(new CategoryItem { Name = "Memes" });
        lib.Sounds.Add(new SoundItem
        {
            Name = "Airhorn", FileName = "x.wav", DurationSeconds = 1.5f, Volume = 1.4f,
            CategoryId = lib.Categories[0].Id,
            Hotkey = new HotkeyBinding { Modifiers = HotkeyBinding.ModAlt, VirtualKey = 0x51 }
        });
        var back = JsonSerializer.Deserialize<LibraryData>(JsonSerializer.Serialize(lib))!;
        Assert.Equal("Airhorn", back.Sounds[0].Name);
        Assert.Equal(lib.Categories[0].Id, back.Sounds[0].CategoryId);
        Assert.Equal("Alt + Q", back.Sounds[0].Hotkey!.ToString());
    }

    [Fact]
    public void Languages_HaveTheSameKeys()
    {
        Assert.Empty(Loc.MissingTranslations());
    }

    [Fact]
    public void Language_SwitchesDirection()
    {
        try
        {
            Loc.I.Language = "ar";
            Assert.Equal(FlowDirection.RightToLeft, Loc.I.FlowDirection);
            Assert.False(string.IsNullOrWhiteSpace(Loc.I["stopAll"]));
            Loc.I.Language = "en";
            Assert.Equal(FlowDirection.LeftToRight, Loc.I.FlowDirection);
            Assert.Equal("Stop all", Loc.I["stopAll"]);
        }
        finally { Loc.I.Language = "en"; }
    }

    [Fact]
    public void VirtualCableDetection()
    {
        Assert.True(AudioEngine.IsVirtualCable("CABLE Input (VB-Audio Virtual Cable)"));
        Assert.True(AudioEngine.IsVirtualCable("CABLE Output (VB-Audio Virtual Cable)"));
        Assert.False(AudioEngine.IsVirtualCable("Speakers (Realtek High Definition Audio)"));
    }

    [Fact]
    public void Engine_WithoutDevices_DoesNotThrow_AndCanBeDisposed()
    {
        // CI machines normally have no audio hardware: every failure must be handled quietly.
        var ex = Record.Exception(() =>
        {
            using var engine = new AudioEngine();
            engine.Apply(new EngineConfig("default", "auto", "default", 40));
            engine.Play(Guid.NewGuid(), new short[480], 1f);
            engine.StopAll();
            engine.SetSoundboardVolume(0.5f);
            engine.Retry();
        });
        Assert.Null(ex);
    }
}
