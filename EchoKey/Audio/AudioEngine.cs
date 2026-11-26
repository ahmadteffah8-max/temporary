using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EchoKey.Audio;

public sealed record DeviceInfo(string Id, string Name);

public enum EngineState { Stopped, Running, NoOutputDevice, Error }

/// <summary>Owns the WASAPI output (virtual cable), the microphone capture and the preview output.</summary>
public sealed class AudioEngine : IDisposable
{
    public MixerProvider Mixer { get; } = new();
    readonly MixerProvider _previewMixer = new();

    OutputPipe? _main;
    OutputPipe? _preview;
    WasapiCapture? _capture;
    MMDevice? _micDevice;
    int _previewIdleTicks;

    public EngineState State { get; private set; } = EngineState.Stopped;
    public string? LastError { get; private set; }
    public string? OutputName { get; private set; }
    public string? MicName { get; private set; }
    /// <summary>"virtualCableAsMic" or "micFailed" when the microphone could not be used.</summary>
    public string? MicWarning { get; private set; }
    public string? MicWarningDetail { get; private set; }

    public bool IsRunning => State == EngineState.Running;
    public bool NeedsRestart => State == EngineState.NoOutputDevice || State == EngineState.Error;

    // ---------- device helpers ----------

    public static bool IsVirtualCable(string name) =>
        name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);

    static bool IsCableInput(string name) =>
        name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase);

    public static List<DeviceInfo> GetRenderDevices() => Enumerate(DataFlow.Render, false);

    /// <summary>Real capture devices only (the virtual cable is excluded to prevent a feedback loop).</summary>
    public static List<DeviceInfo> GetCaptureDevices() => Enumerate(DataFlow.Capture, true);

    static List<DeviceInfo> Enumerate(DataFlow flow, bool excludeCable)
    {
        var result = new List<DeviceInfo>();
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (d)
                {
                    string name = d.FriendlyName;
                    if (excludeCable && IsVirtualCable(name)) continue;
                    result.Add(new DeviceInfo(d.ID, name));
                }
            }
        }
        catch { /* no audio service */ }
        return result;
    }

    /// <summary>Friendly name of the VB-CABLE playback endpoint, or null when it is not installed.</summary>
    public static string? GetCableName()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (d)
                {
                    if (IsCableInput(d.FriendlyName)) return d.FriendlyName;
                }
            }
        }
        catch { }
        return null;
    }

    static MMDevice? FindCableRender(MMDeviceEnumerator en)
    {
        MMDevice? found = null;
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (found == null && IsCableInput(d.FriendlyName)) found = d;
            else d.Dispose();
        }
        return found;
    }

    // ---------- lifecycle ----------

    public void Start(string? micId, string? outId, int latencyMs)
    {
        StopInternal();
        LastError = null;
        MicWarning = null;
        MicWarningDetail = null;
        OutputName = null;
        MicName = null;
        int latency = Math.Clamp(latencyMs, 10, 300);

        try
        {
            using var en = new MMDeviceEnumerator();

            MMDevice? outDev = null;
            if (string.IsNullOrEmpty(outId) || outId == "auto")
                outDev = FindCableRender(en);
            else
            {
                try { outDev = en.GetDevice(outId); } catch { outDev = null; }
            }

            if (outDev == null)
            {
                State = EngineState.NoOutputDevice;
                return;
            }

            OutputName = outDev.FriendlyName;
            _main = new OutputPipe(outDev, Mixer, latency); // pipe owns outDev
            _main.Faulted += OnPipeFaulted;
            _main.Start();
            State = EngineState.Running;

            StartMic(en, micId);
        }
        catch (Exception ex)
        {
            StopInternal();
            LastError = ex.Message;
            State = EngineState.Error;
        }
    }

    void StartMic(MMDeviceEnumerator en, string? micId)
    {
        if (micId == "none") return;
        MMDevice? dev = null;
        try
        {
            if (string.IsNullOrEmpty(micId) || micId == "default")
            {
                try { dev = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); }
                catch { return; } // no microphone at all: soundboard still works
            }
            else
            {
                dev = en.GetDevice(micId);
            }

            if (IsVirtualCable(dev.FriendlyName))
            {
                MicWarning = "virtualCableAsMic";
                dev.Dispose();
                return;
            }

            var cap = new WasapiCapture(dev, true, 20);
            var buf = new BufferedWaveProvider(cap.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(400),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            cap.DataAvailable += (_, a) => buf.AddSamples(a.Buffer, 0, a.BytesRecorded);
            cap.RecordingStopped += OnCaptureStopped;

            ISampleProvider sp = buf.ToSampleProvider();
            int ch = sp.WaveFormat.Channels;
            if (ch == 1)
                sp = new MonoToStereoSampleProvider(sp);
            else if (ch > 2)
            {
                var mux = new MultiplexingSampleProvider(new[] { sp }, 2);
                mux.ConnectInputToOutput(0, 0);
                mux.ConnectInputToOutput(1, 1);
                sp = mux;
            }
            if (sp.WaveFormat.SampleRate != MixerProvider.SampleRate)
                sp = new WdlResamplingSampleProvider(sp, MixerProvider.SampleRate);

            _micDevice = dev;
            _capture = cap;
            MicName = dev.FriendlyName;
            Mixer.SetMic(new MicSource(sp, buf));
            cap.StartRecording();
        }
        catch (Exception ex)
        {
            MicWarning = "micFailed";
            MicWarningDetail = ex.Message;
            StopMic();
            dev?.Dispose();
        }
    }

    void StopMic()
    {
        Mixer.SetMic(null);
        var cap = _capture;
        _capture = null;
        if (cap != null)
        {
            try { cap.StopRecording(); } catch { }
            try { cap.Dispose(); } catch { }
        }
        _micDevice?.Dispose();
        _micDevice = null;
    }

    void StopInternal()
    {
        StopMic();
        var main = _main;
        _main = null;
        if (main != null)
        {
            main.Faulted -= OnPipeFaulted;
            main.Dispose();
        }
        Mixer.Clear();
        State = EngineState.Stopped;
    }

    void OnPipeFaulted(string message)
    {
        LastError = message;
        State = EngineState.Error;
    }

    void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, _capture)) return; // we stopped it ourselves
        if (e.Exception != null)
        {
            LastError = e.Exception.Message;
            State = EngineState.Error;
        }
    }

    // ---------- preview (headphones / default speakers) ----------

    public void Preview(Guid id, short[] data, float gain)
    {
        if (_preview == null)
        {
            using var en = new MMDeviceEnumerator();
            MMDevice? dev = null;
            try
            {
                dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (IsVirtualCable(dev.FriendlyName))
                {
                    dev.Dispose();
                    dev = null;
                    foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    {
                        if (dev == null && !IsVirtualCable(d.FriendlyName)) dev = d;
                        else d.Dispose();
                    }
                }
            }
            catch { dev?.Dispose(); dev = null; }
            if (dev == null) throw new InvalidOperationException("No playback device available.");

            _preview = new OutputPipe(dev, _previewMixer, 60);
            _preview.Start();
        }
        _previewIdleTicks = 0;
        _previewMixer.Clear();
        _previewMixer.Play(id, data, gain);
    }

    public void StopPreview() => _previewMixer.StopAll();

    /// <summary>Called every ~2 s: closes the preview device once it has been idle for a while.</summary>
    public void Housekeeping()
    {
        if (_preview == null) return;
        if (_previewMixer.ActiveCount == 0)
        {
            if (++_previewIdleTicks >= 3)
            {
                _preview.Dispose();
                _preview = null;
                _previewIdleTicks = 0;
            }
        }
        else _previewIdleTicks = 0;
    }

    public void Dispose()
    {
        StopInternal();
        _preview?.Dispose();
        _preview = null;
    }
}

/// <summary>A WASAPI shared-mode output fed by a 48 kHz stereo float mixer (converted to the device format).</summary>
internal sealed class OutputPipe : IDisposable
{
    readonly MMDevice _device;
    readonly WasapiOut _out;
    bool _disposed;

    public event Action<string>? Faulted;

    public OutputPipe(MMDevice device, ISampleProvider source, int latencyMs)
    {
        _device = device;
        try
        {
            var mix = device.AudioClient.MixFormat;
            ISampleProvider chain = source;
            if (mix.SampleRate != MixerProvider.SampleRate)
                chain = new WdlResamplingSampleProvider(chain, mix.SampleRate);

            if (mix.Channels == 1)
            {
                chain = new StereoToMonoSampleProvider(chain) { LeftVolume = 0.5f, RightVolume = 0.5f };
            }
            else if (mix.Channels > 2)
            {
                var mux = new MultiplexingSampleProvider(new[] { chain }, mix.Channels);
                mux.ConnectInputToOutput(0, 0);
                mux.ConnectInputToOutput(1, 1);
                chain = mux;
            }

            _out = new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMs);
            _out.Init(new SampleToWaveProvider(chain));
            _out.PlaybackStopped += (_, e) =>
            {
                if (!_disposed) Faulted?.Invoke(e.Exception?.Message ?? "Output stopped unexpectedly.");
            };
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    public void Start() => _out.Play();

    public void Dispose()
    {
        _disposed = true;
        try { _out.Stop(); } catch { }
        try { _out.Dispose(); } catch { }
        try { _device.Dispose(); } catch { }
    }
}
