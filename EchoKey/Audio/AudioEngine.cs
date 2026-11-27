using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EchoKey.Audio;

public sealed record DeviceInfo(string Id, string Name);

public enum EngineState { Stopped, Running, NoOutputDevice, Error }

public sealed record EngineConfig(string? MicId, string? OutputId, string? MonitorId, int LatencyMs);

/// <summary>
/// Two independent output channels that run at the same time:
///   1. Virtual mic  : real microphone + soundboard  ->  VB-CABLE (what other programs hear)
///   2. My speakers  : soundboard ONLY               ->  my headphones/speakers (never the microphone)
/// Each channel has its own mixer and its own WASAPI stream. A sound that is played is added to both.
/// Devices are only ever stopped after the stream has faded to silence.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    /// <summary>Channel 1: microphone + soundboard, sent to the virtual cable.</summary>
    public MixerProvider Mixer { get; } = new();
    /// <summary>Channel 2: soundboard only, sent to the user's speakers. Never has a microphone.</summary>
    public MixerProvider MonitorMixer { get; } = new();

    OutputPipe? _main;
    OutputPipe? _monitor;
    WasapiCapture? _capture;
    MMDevice? _micDevice;
    EngineConfig? _cfg;

    public EngineState State { get; private set; } = EngineState.Stopped;
    public EngineState MonitorState { get; private set; } = EngineState.Stopped;
    public string? LastError { get; private set; }
    public string? MonitorError { get; private set; }
    public string? OutputName { get; private set; }
    public string? MonitorName { get; private set; }
    public string? MicName { get; private set; }
    /// <summary>"virtualCableAsMic", "micNeedsCable" or "micFailed" when the microphone could not be used.</summary>
    public string? MicWarning { get; private set; }
    public string? MicWarningDetail { get; private set; }
    /// <summary>"monitorIsCable" when the chosen speakers device is a virtual cable.</summary>
    public string? MonitorWarning { get; private set; }

    /// <summary>When false, normal sounds are not played on the speakers (the preview button still is).</summary>
    public bool MonitorEnabled { get; set; } = true;

    public bool IsRunning => State == EngineState.Running;
    public bool MonitorRunning => MonitorState == EngineState.Running;
    public bool CanPlay => IsRunning || (MonitorEnabled && MonitorRunning);

    public bool NeedsRestart =>
        State == EngineState.NoOutputDevice || State == EngineState.Error ||
        MonitorState == EngineState.NoOutputDevice || MonitorState == EngineState.Error;

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

    // ---------- configuration / lifecycle ----------

    /// <summary>
    /// Applies a configuration. Only the parts that actually changed are restarted, and every restarted stream is
    /// faded to silence first, so changing settings does not click, buzz or leave a sound hanging.
    /// </summary>
    public void Apply(EngineConfig cfg, bool forceMain = false, bool forceMonitor = false)
    {
        var old = _cfg;
        _cfg = cfg;
        int latency = Math.Clamp(cfg.LatencyMs, 10, 300);

        bool mainChanged = forceMain || old == null || old.OutputId != cfg.OutputId || old.LatencyMs != cfg.LatencyMs;
        bool monitorChanged = forceMonitor || old == null || old.MonitorId != cfg.MonitorId || old.LatencyMs != cfg.LatencyMs
                              || old.OutputId != cfg.OutputId; // the monitor must not duplicate the main device
        bool micChanged = mainChanged || old == null || old.MicId != cfg.MicId;

        // 1) quiet phase: fade the affected streams to silence, then stop them
        var fading = new List<MixerProvider>();
        if (mainChanged && _main != null && State == EngineState.Running) { Mixer.GateOpen = false; fading.Add(Mixer); }
        if (monitorChanged && _monitor != null && MonitorState == EngineState.Running) { MonitorMixer.GateOpen = false; fading.Add(MonitorMixer); }
        if (!mainChanged && micChanged && _capture != null) Mixer.MicSuspended = true;
        if (fading.Count > 0 || Mixer.MicSuspended) Thread.Sleep(Math.Clamp(latency * 2 + 50, 100, 600));

        if (mainChanged) StopMain();
        else if (micChanged) StopMic();
        if (monitorChanged) StopMonitor();

        // 2) start phase
        try
        {
            using var en = new MMDeviceEnumerator();
            if (mainChanged) StartMain(en, cfg, latency);
            if (monitorChanged) StartMonitor(en, cfg, latency);
            if (micChanged) StartMic(en, cfg.MicId);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            State = EngineState.Error;
        }
        Mixer.MicSuspended = false;
    }

    /// <summary>Retries the streams that are missing or faulted (device unplugged, VB-CABLE just installed...).</summary>
    public void Retry()
    {
        if (_cfg == null) return;
        bool main = State == EngineState.NoOutputDevice || State == EngineState.Error;
        bool mon = MonitorState == EngineState.NoOutputDevice || MonitorState == EngineState.Error;
        if (main || mon) Apply(_cfg, main, mon);
    }

    void StartMain(MMDeviceEnumerator en, EngineConfig cfg, int latency)
    {
        LastError = null;
        MicWarning = null;
        MicWarningDetail = null;
        OutputName = null;

        MMDevice? dev = null;
        if (string.IsNullOrEmpty(cfg.OutputId) || cfg.OutputId == "auto")
            dev = FindCableRender(en);
        else
        {
            try { dev = en.GetDevice(cfg.OutputId); } catch { dev = null; }
        }

        if (dev == null)
        {
            State = EngineState.NoOutputDevice;
            return;
        }

        try
        {
            OutputName = dev.FriendlyName;
            _main = new OutputPipe(dev, Mixer, latency); // pipe owns dev
            _main.Faulted += OnMainFaulted;
            _main.Start();
            State = EngineState.Running;
        }
        catch (Exception ex)
        {
            StopMain();
            LastError = ex.Message;
            State = EngineState.Error;
        }
    }

    void StartMonitor(MMDeviceEnumerator en, EngineConfig cfg, int latency)
    {
        MonitorError = null;
        MonitorWarning = null;
        MonitorName = null;

        MMDevice? dev = null;
        try
        {
            if (string.IsNullOrEmpty(cfg.MonitorId) || cfg.MonitorId == "default")
            {
                dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (IsVirtualCable(dev.FriendlyName) || dev.ID == _main?.DeviceId)
                {
                    // Windows' default playback device is the cable (or the main output): use the first real device instead.
                    dev.Dispose();
                    dev = null;
                    foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    {
                        if (dev == null && !IsVirtualCable(d.FriendlyName) && d.ID != _main?.DeviceId) dev = d;
                        else d.Dispose();
                    }
                }
            }
            else
            {
                dev = en.GetDevice(cfg.MonitorId);
                if (IsVirtualCable(dev.FriendlyName))
                {
                    MonitorWarning = "monitorIsCable";
                    dev.Dispose();
                    MonitorState = EngineState.Stopped;
                    return;
                }
                if (dev.ID == _main?.DeviceId)
                {
                    dev.Dispose(); // same device as the virtual mic output: would play every sound twice
                    MonitorState = EngineState.Stopped;
                    return;
                }
            }
        }
        catch
        {
            dev?.Dispose();
            dev = null;
        }

        if (dev == null)
        {
            MonitorState = EngineState.NoOutputDevice;
            return;
        }

        try
        {
            MonitorName = dev.FriendlyName;
            _monitor = new OutputPipe(dev, MonitorMixer, latency);
            _monitor.Faulted += OnMonitorFaulted;
            _monitor.Start();
            MonitorState = EngineState.Running;
        }
        catch (Exception ex)
        {
            StopMonitor();
            MonitorError = ex.Message;
            MonitorState = EngineState.Error;
        }
    }

    void StartMic(MMDeviceEnumerator en, string? micId)
    {
        MicName = null;
        MicWarning = null;
        MicWarningDetail = null;
        if (micId == "none" || _main == null) return;

        // The microphone is only ever sent to a virtual cable. Any other output device would play the user's own
        // voice back on the speakers = feedback/echo.
        if (!IsVirtualCable(OutputName ?? ""))
        {
            MicWarning = "micNeedsCable";
            return;
        }

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
                BufferDuration = TimeSpan.FromMilliseconds(500),
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

    // Stop* methods are called after the stream has already faded to silence.

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
        MicName = null;
    }

    void StopMain()
    {
        StopMic();
        var p = _main;
        _main = null;
        if (p != null)
        {
            p.Faulted -= OnMainFaulted;
            p.Dispose();
        }
        Mixer.Clear();
        State = EngineState.Stopped;
        OutputName = null;
    }

    void StopMonitor()
    {
        var p = _monitor;
        _monitor = null;
        if (p != null)
        {
            p.Faulted -= OnMonitorFaulted;
            p.Dispose();
        }
        MonitorMixer.Clear();
        MonitorState = EngineState.Stopped;
        MonitorName = null;
    }

    void OnMainFaulted(string message)
    {
        LastError = message;
        State = EngineState.Error;
    }

    void OnMonitorFaulted(string message)
    {
        MonitorError = message;
        MonitorState = EngineState.Error;
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

    // ---------- playback ----------

    /// <summary>Plays a sound on the virtual mic channel and (if enabled) on the speakers channel at the same time.</summary>
    public void Play(Guid id, short[] data, float gain)
    {
        if (IsRunning) Mixer.Play(id, data, gain);
        if (MonitorEnabled && MonitorRunning) MonitorMixer.Play(id, data, gain);
    }

    /// <summary>Speakers only. Never reaches the virtual microphone and never interferes with normal playback.</summary>
    public void Preview(Guid id, short[] data, float gain)
    {
        if (!MonitorRunning) throw new InvalidOperationException("No speakers/headphones device available.");
        MonitorMixer.Play(id, data, gain);
    }

    public void StopAll()
    {
        Mixer.StopAll();
        MonitorMixer.StopAll();
    }

    public void SetSoundboardVolume(float v)
    {
        Mixer.SoundboardVolume = v;
        MonitorMixer.SoundboardVolume = v;
    }

    /// <summary>Active (non-fading) voices per sound; the virtual-mic channel is the reference, the speakers channel when it is not running.</summary>
    public int Snapshot(Dictionary<Guid, int> counts) =>
        IsRunning ? Mixer.Snapshot(counts) : MonitorMixer.Snapshot(counts);

    public void Dispose()
    {
        var fading = 0;
        if (_main != null && State == EngineState.Running) { Mixer.GateOpen = false; fading++; }
        if (_monitor != null && MonitorState == EngineState.Running) { MonitorMixer.GateOpen = false; fading++; }
        if (fading > 0) Thread.Sleep(Math.Clamp((_cfg?.LatencyMs ?? 40) * 2 + 50, 100, 600));
        StopMain();
        StopMonitor();
    }
}

/// <summary>
/// Converts the float mixer output to the exact format of the audio device (32-bit float, or 16/24/32-bit PCM),
/// so WASAPI never has to guess or run its own converter. Always returns a full buffer (padded with silence).
/// </summary>
internal sealed class DeviceFormatProvider : IWaveProvider
{
    readonly ISampleProvider _src;
    readonly bool _float;
    readonly int _bytes;
    float[] _buf = new float[4096];

    public WaveFormat WaveFormat { get; }

    DeviceFormatProvider(ISampleProvider src, WaveFormat format, bool isFloat, int bytes)
    {
        _src = src;
        WaveFormat = format;
        _float = isFloat;
        _bytes = bytes;
    }

    /// <summary>Returns null when the device format is not one we can write directly.</summary>
    public static DeviceFormatProvider? TryCreate(ISampleProvider src, WaveFormat mix)
    {
        WaveFormat std = mix is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : mix;
        bool isFloat = std.Encoding == WaveFormatEncoding.IeeeFloat;
        bool isPcm = std.Encoding == WaveFormatEncoding.Pcm;
        int bits = mix.BitsPerSample;

        if (isFloat) { if (bits != 32) return null; }
        else if (!(isPcm && (bits == 16 || bits == 24 || bits == 32))) return null;

        if (src.WaveFormat.Channels != mix.Channels || src.WaveFormat.SampleRate != mix.SampleRate) return null;
        return new DeviceFormatProvider(src, mix, isFloat, bits / 8);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int samples = count / _bytes;
        if (_buf.Length < samples) _buf = new float[samples];

        int n = _src.Read(_buf, 0, samples);
        if (n < samples) Array.Clear(_buf, Math.Max(0, n), samples - Math.Max(0, n));

        int o = offset;
        for (int i = 0; i < samples; i++)
        {
            float f = _buf[i];
            if (float.IsNaN(f)) f = 0f;
            else if (f > 1f) f = 1f;
            else if (f < -1f) f = -1f;

            if (_float)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(o, 4), f);
            }
            else if (_bytes == 2)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(o, 2), (short)Math.Round(f * 32767f));
            }
            else if (_bytes == 3)
            {
                int v = (int)Math.Round(f * 8388607f);
                buffer[o] = (byte)v;
                buffer[o + 1] = (byte)(v >> 8);
                buffer[o + 2] = (byte)(v >> 16);
            }
            else
            {
                int v = (int)Math.Clamp(Math.Round(f * 2147483647.0), int.MinValue, int.MaxValue);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(o, 4), v);
            }
            o += _bytes;
        }
        return samples * _bytes;
    }
}

/// <summary>A WASAPI shared-mode output fed by a 48 kHz stereo float mixer (converted to the device format).</summary>
internal sealed class OutputPipe : IDisposable
{
    readonly MMDevice _device;
    readonly WasapiOut _out;
    readonly MixerProvider _mixer;
    bool _disposed;

    public string DeviceId { get; }
    public event Action<string>? Faulted;

    public OutputPipe(MMDevice device, MixerProvider mixer, int latencyMs)
    {
        _device = device;
        _mixer = mixer;
        DeviceId = device.ID;
        try
        {
            var mix = device.AudioClient.MixFormat;
            ISampleProvider chain = mixer;
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

            // Feed the device in EXACTLY its own format (same rate, channels, bit depth). If the format differs from
            // what the Windows audio engine expects, NAudio silently falls back to a DMO converter, and a wrong
            // conversion is heard as loud static instead of the sound. Unknown formats keep the old float path.
            IWaveProvider wave = (IWaveProvider?)DeviceFormatProvider.TryCreate(chain, mix) ?? new SampleToWaveProvider(chain);

            _out = new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMs);
            _out.Init(wave);
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

    /// <summary>Starts the device and fades the stream in from silence.</summary>
    public void Start()
    {
        _out.Play();
        _mixer.GateOpen = true;
    }

    public void Dispose()
    {
        _disposed = true;
        _mixer.GateOpen = false; // callers fade first; this is only a safety net
        try { _out.Stop(); } catch { }
        try { _out.Dispose(); } catch { }
        try { _device.Dispose(); } catch { }
    }
}
