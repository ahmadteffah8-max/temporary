using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using EchoKey.Audio;
using EchoKey.Core;
using EchoKey.Hotkeys;

namespace EchoKey.ViewModels;

/// <summary>Application controller: library, audio engine, hotkeys and all the toggles.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    readonly Storage _storage = new();
    readonly SoundCache _cache = new();
    readonly HotkeyManager _hotkeys = new();
    readonly List<SoundViewModel> _all = new();
    readonly Dictionary<Guid, int> _counts = new();
    readonly DispatcherTimer _uiTimer;
    readonly DispatcherTimer _houseTimer;
    readonly DispatcherTimer _statusTimer;

    string? _cableName;
    bool _windowVisible;
    int _houseTick;
    bool _noOutputShown;
    string? _lastEngineError;
    string? _lastMonitorError;

    public AudioEngine Engine { get; } = new();
    public AppSettings Settings => _storage.Settings;
    public bool Exiting { get; set; }

    public ObservableCollection<SoundViewModel> VisibleSounds { get; } = new();
    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    public MainViewModel()
    {
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _uiTimer.Tick += (_, _) => UiTick();
        _houseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _houseTimer.Tick += (_, _) => HouseTick();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusMessage = ""; };
    }

    // ------------------------------------------------------------------ startup

    public void Initialize()
    {
        _storage.Load();
        var s = Settings;

        if (s.Language == null)
            s.Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "ar" : "en";
        Loc.I.Language = s.Language;
        Loc.I.Changed += OnLocChanged;

        _sbEnabled = s.SoundboardEnabled;
        _hkEnabled = s.HotkeysEnabled;
        _micEnabled = s.MicEnabled;
        _micVol = s.MicVolume;
        _sbVol = s.SoundboardVolume;
        Engine.Mixer.MicEnabled = _micEnabled;
        Engine.Mixer.MicVolume = (float)_micVol;
        Engine.SetSoundboardVolume((float)_sbVol);
        Engine.MonitorEnabled = s.MonitorEnabled;

        foreach (var item in _storage.Library.Sounds.OrderBy(x => x.Order))
            _all.Add(CreateVm(item));

        BuildCategories();
        SelectedCategory = Categories[0];

        StartEngine();
        ApplyHotkeys();

        _ = _cache.PreloadAsync(_all.Select(v => v.Item));
        _houseTimer.Start();
    }

    SoundViewModel CreateVm(SoundItem item) => new(item, _storage.RequestSave);

    // ------------------------------------------------------------------ toggles & volumes

    bool _sbEnabled = true, _hkEnabled = true, _micEnabled = true;
    double _micVol = 1, _sbVol = 1;

    public bool SoundboardEnabled
    {
        get => _sbEnabled;
        set
        {
            if (!Set(ref _sbEnabled, value)) return;
            Settings.SoundboardEnabled = value;
            if (!value) StopAll();
            _storage.RequestSave();
        }
    }

    /// <summary>Independent from SoundboardEnabled: when off, the keys are released to the system and games.</summary>
    public bool HotkeysEnabled
    {
        get => _hkEnabled;
        set
        {
            if (!Set(ref _hkEnabled, value)) return;
            Settings.HotkeysEnabled = value;
            ApplyHotkeys();
            _storage.RequestSave();
        }
    }

    public bool MicEnabled
    {
        get => _micEnabled;
        set
        {
            if (!Set(ref _micEnabled, value)) return;
            Settings.MicEnabled = value;
            Engine.Mixer.MicEnabled = value;
            _storage.RequestSave();
        }
    }

    public double MicVolume
    {
        get => _micVol;
        set
        {
            if (!Set(ref _micVol, value)) return;
            Settings.MicVolume = (float)value;
            Engine.Mixer.MicVolume = (float)value;
            Raise(nameof(MicVolumeText));
            _storage.RequestSave();
        }
    }

    public double SoundboardVolume
    {
        get => _sbVol;
        set
        {
            if (!Set(ref _sbVol, value)) return;
            Settings.SoundboardVolume = (float)value;
            Engine.SetSoundboardVolume((float)value);
            Raise(nameof(SoundboardVolumeText));
            _storage.RequestSave();
        }
    }

    public string MicVolumeText => ((int)Math.Round(_micVol * 100)).ToString(CultureInfo.InvariantCulture) + "%";
    public string SoundboardVolumeText => ((int)Math.Round(_sbVol * 100)).ToString(CultureInfo.InvariantCulture) + "%";

    // ------------------------------------------------------------------ status / cable

    string _status = "";
    public string StatusMessage
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value)) Raise(nameof(HasStatus));
        }
    }
    public bool HasStatus => _status.Length > 0;

    public void Notify(string message)
    {
        StatusMessage = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    public bool CableFound => _cableName != null;
    public string CableText => _cableName != null ? Loc.I.Format("cableOk", _cableName) : Loc.I["cableMissing"];

    public void RefreshCable()
    {
        _cableName = AudioEngine.GetCableName();
        Raise(nameof(CableFound));
        Raise(nameof(CableText));
    }

    int _activeTotal;
    public string ActiveText => Loc.I.Format("playingNow", _activeTotal);

    // ------------------------------------------------------------------ engine

    EngineConfig CurrentConfig() =>
        new(Settings.MicDeviceId, Settings.OutputDeviceId, Settings.MonitorDeviceId, Settings.OutputLatencyMs);

    void StartEngine()
    {
        Engine.Apply(CurrentConfig());
        ReportEngine();
    }

    /// <summary>Applies new audio settings. Only the parts that changed are restarted, each one faded out first.</summary>
    public void ApplyAudioSettings(string micId, string outId, string monitorId, bool monitorEnabled, int latencyMs)
    {
        Settings.MicDeviceId = micId;
        Settings.OutputDeviceId = outId;
        Settings.MonitorDeviceId = monitorId;
        Settings.MonitorEnabled = monitorEnabled;
        Settings.OutputLatencyMs = latencyMs;
        Engine.MonitorEnabled = monitorEnabled;
        _storage.RequestSave();
        _noOutputShown = false;
        _lastEngineError = null;
        _lastMonitorError = null;
        StartEngine();
    }

    void ReportEngine()
    {
        RefreshCable();
        switch (Engine.State)
        {
            case EngineState.NoOutputDevice:
                if (!_noOutputShown) { Notify(Loc.I["noOutput"]); _noOutputShown = true; }
                break;
            case EngineState.Error:
                if (_lastEngineError != Engine.LastError)
                {
                    _lastEngineError = Engine.LastError;
                    Notify(Loc.I.Format("engineError", Engine.LastError));
                }
                break;
            case EngineState.Running:
                _noOutputShown = false;
                _lastEngineError = null;
                break;
        }

        if (Engine.MonitorState == EngineState.Error)
        {
            if (_lastMonitorError != Engine.MonitorError)
            {
                _lastMonitorError = Engine.MonitorError;
                Notify(Loc.I.Format("monitorError", Engine.MonitorError));
            }
        }
        else if (Engine.MonitorState == EngineState.Running) _lastMonitorError = null;

        if (Engine.MicWarning == "virtualCableAsMic") Notify(Loc.I["micIsCable"]);
        else if (Engine.MicWarning == "micNeedsCable") Notify(Loc.I["micNeedsCable"]);
        else if (Engine.MicWarning == "micFailed") Notify(Loc.I.Format("micFailed", Engine.MicWarningDetail));
        if (Engine.MonitorWarning == "monitorIsCable") Notify(Loc.I["monitorIsCable"]);
    }

    void HouseTick()
    {
        if (Engine.NeedsRestart)
        {
            // device unplugged / VB-CABLE just installed / driver restarted: retry only the broken stream, quietly
            Engine.Retry();
            ReportEngine();
        }
        else if (_windowVisible && ++_houseTick % 5 == 0)
        {
            RefreshCable();
        }
    }

    public bool WindowVisible
    {
        set
        {
            _windowVisible = value;
            if (value) { UiTick(); _uiTimer.Start(); RefreshCable(); }
            else _uiTimer.Stop();
        }
    }

    void UiTick()
    {
        int total = Engine.Snapshot(_counts);
        foreach (var v in _all)
        {
            _counts.TryGetValue(v.Item.Id, out int c);
            v.ActiveCount = c;
        }
        if (_activeTotal != total)
        {
            _activeTotal = total;
            Raise(nameof(ActiveText));
        }
    }

    // ------------------------------------------------------------------ playback

    public void PlaySound(SoundViewModel vm)
    {
        if (!_sbEnabled) return;
        if (!Engine.CanPlay)
        {
            Notify(Loc.I["noOutput"]);
            return;
        }

        var item = vm.Item;
        var task = _cache.GetAsync(item);
        if (task.IsCompletedSuccessfully)
        {
            Engine.Play(item.Id, task.Result, item.Volume); // instant, every press = new overlapping voice
            return;
        }
        _ = PlayWhenReady(item, task);
    }

    async Task PlayWhenReady(SoundItem item, Task<short[]> task)
    {
        try
        {
            var data = await task;
            if (_sbEnabled && Engine.CanPlay) Engine.Play(item.Id, data, item.Volume);
        }
        catch (Exception ex)
        {
            Notify(Loc.I.Format("playFailed", item.Name, ex.Message));
        }
    }

    public async void PreviewSound(SoundViewModel vm)
    {
        try
        {
            var data = await _cache.GetAsync(vm.Item);
            Engine.Preview(vm.Item.Id, data, vm.Item.Volume); // speakers only; master volume is applied by the mixer
        }
        catch (Exception ex)
        {
            Notify(Loc.I.Format("playFailed", vm.Item.Name, ex.Message));
        }
    }

    public void StopAll()
    {
        Engine.StopAll();
    }

    // ------------------------------------------------------------------ hotkeys

    void ApplyHotkeys()
    {
        _hotkeys.UnregisterAll();
        foreach (var v in _all) v.HotkeyFailed = false;
        if (!_hkEnabled) return;

        var stop = Settings.StopAllHotkey;
        if (stop != null && _hotkeys.Register(stop, StopAll) == 0)
            Notify(Loc.I.Format("registerFail", stop.ToString()));

        foreach (var v in _all)
        {
            var hk = v.Item.Hotkey;
            if (hk == null) continue;
            var target = v;
            int id = _hotkeys.Register(hk, () => PlaySound(target));
            if (id == 0)
            {
                v.HotkeyFailed = true;
                Notify(Loc.I.Format("registerFail", hk.ToString()));
            }
        }
    }

    /// <summary>Name of whatever already uses this shortcut (another sound or "Stop all"), or null when it is free.</summary>
    public string? FindConflict(HotkeyBinding hk, SoundViewModel? exceptSound, bool exceptStopAll)
    {
        var other = _all.FirstOrDefault(o => o != exceptSound && hk.SameAs(o.Item.Hotkey));
        if (other != null) return other.Item.Name;
        if (!exceptStopAll && hk.SameAs(Settings.StopAllHotkey)) return Loc.I["stopAll"];
        return null;
    }

    /// <summary>Returns false (and shows a warning) when another sound or Stop-all already uses the shortcut.</summary>
    public bool SetHotkey(SoundViewModel vm, HotkeyBinding? hk)
    {
        if (hk != null)
        {
            string? other = FindConflict(hk, vm, false);
            if (other != null)
            {
                Notify(Loc.I.Format("conflict", other));
                return false;
            }
        }
        vm.Item.Hotkey = hk;
        vm.RefreshHotkey();
        ApplyHotkeys();
        _storage.RequestSave();
        return true;
    }

    public string StopAllHotkeyText => Settings.StopAllHotkey?.ToString() ?? "";
    public string StopAllHotkeyLabel => Settings.StopAllHotkey != null ? Settings.StopAllHotkey.ToString() : "＋ " + Loc.I["setHotkey"];
    public string StopAllTooltip =>
        Settings.StopAllHotkey != null ? Loc.I["stopAll"] + "  (" + Settings.StopAllHotkey + ")" : Loc.I["stopAll"];

    /// <summary>Saves the Stop-all shortcut (null removes it). Returns the conflicting name when it cannot be used.</summary>
    public string? SetStopAllHotkey(HotkeyBinding? hk)
    {
        if (hk != null)
        {
            string? other = FindConflict(hk, null, true);
            if (other != null) return other;
        }
        Settings.StopAllHotkey = hk;
        Raise(nameof(StopAllHotkeyText));
        Raise(nameof(StopAllHotkeyLabel));
        Raise(nameof(StopAllTooltip));
        ApplyHotkeys();
        _storage.RequestSave();
        return null;
    }

    // ------------------------------------------------------------------ library

    CategoryViewModel? _selected;
    public CategoryViewModel? SelectedCategory
    {
        get => _selected;
        set
        {
            if (value == null) return; // ListBox sets null while the list is rebuilt
            if (Set(ref _selected, value)) RefreshVisible();
        }
    }

    public bool IsEmpty => VisibleSounds.Count == 0;

    void BuildCategories()
    {
        var keepKind = _selected?.Kind;
        var keepId = _selected?.Id;
        Categories.Clear();
        Categories.Add(new CategoryViewModel(CategoryKind.All, null, _storage.RequestSave));
        Categories.Add(new CategoryViewModel(CategoryKind.Uncategorized, null, _storage.RequestSave));
        foreach (var c in _storage.Library.Categories.OrderBy(x => x.Order))
            Categories.Add(new CategoryViewModel(CategoryKind.User, c, _storage.RequestSave));

        var again = Categories.FirstOrDefault(c => c.Kind == keepKind && c.Id == keepId) ?? Categories[0];
        _selected = null;
        SelectedCategory = again;
    }

    void RefreshVisible()
    {
        IEnumerable<SoundViewModel> q = _all;
        var sel = _selected;
        if (sel != null)
        {
            if (sel.Kind == CategoryKind.Uncategorized) q = q.Where(v => v.Item.CategoryId == null);
            else if (sel.Kind == CategoryKind.User) q = q.Where(v => v.Item.CategoryId == sel.Id);
        }
        VisibleSounds.Clear();
        foreach (var v in q.OrderBy(v => v.Item.Order)) VisibleSounds.Add(v);
        Raise(nameof(IsEmpty));
    }

    int NextOrder() => _all.Count == 0 ? 0 : _all.Max(v => v.Item.Order) + 1;

    public async Task ImportAsync(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var p in paths)
        {
            try
            {
                if (Directory.Exists(p))
                    files.AddRange(Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Where(SoundDecoder.IsSupported));
                else if (File.Exists(p) && SoundDecoder.IsSupported(p))
                    files.Add(p);
            }
            catch { /* unreadable folder */ }
        }
        if (files.Count == 0) return;

        Guid? cat = _selected?.Kind == CategoryKind.User ? _selected.Id : null;
        var errors = new List<string>();

        foreach (var file in files)
        {
            try
            {
                double dur = await Task.Run(() => SoundDecoder.GetDuration(file));
                if (dur > SoundDecoder.MaxSeconds) throw new InvalidOperationException(Loc.I["tooLong"]);

                string stored = Guid.NewGuid().ToString("N") + Path.GetExtension(file).ToLowerInvariant();
                string dest = Path.Combine(AppPaths.SoundsDir, stored);
                await Task.Run(() => File.Copy(file, dest, false)); // private copy: survives deleting/moving the original

                var item = new SoundItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FileName = stored,
                    DurationSeconds = dur,
                    CategoryId = cat,
                    Order = NextOrder()
                };
                _storage.Library.Sounds.Add(item);
                _all.Add(CreateVm(item));
                _ = WarmUp(item);
            }
            catch (Exception ex)
            {
                errors.Add(Loc.I.Format("importFailed", Path.GetFileName(file), ex.Message));
            }
        }

        RefreshVisible();
        _storage.RequestSave();
        if (errors.Count > 0) Notify(string.Join("   |   ", errors.Take(2)));
    }

    async Task WarmUp(SoundItem item)
    {
        try { await _cache.GetAsync(item); } catch { }
    }

    public void DeleteSound(SoundViewModel vm)
    {
        _storage.Library.Sounds.Remove(vm.Item);
        _all.Remove(vm);
        _cache.Remove(vm.Item.Id);
        try { File.Delete(Path.Combine(AppPaths.SoundsDir, vm.Item.FileName)); } catch { }
        ApplyHotkeys();
        RefreshVisible();
        _storage.RequestSave();
    }

    public void MoveSound(SoundViewModel vm, int delta)
    {
        int i = VisibleSounds.IndexOf(vm);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= VisibleSounds.Count) return;
        var other = VisibleSounds[j];
        (vm.Item.Order, other.Item.Order) = (other.Item.Order, vm.Item.Order);
        RefreshVisible();
        _storage.RequestSave();
    }

    public void MoveToCategory(SoundViewModel vm, Guid? categoryId)
    {
        vm.Item.CategoryId = categoryId;
        RefreshVisible();
        _storage.RequestSave();
    }

    public void AddCategory(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return;
        var c = new CategoryItem
        {
            Name = name,
            Order = _storage.Library.Categories.Count == 0 ? 0 : _storage.Library.Categories.Max(x => x.Order) + 1
        };
        _storage.Library.Categories.Add(c);
        _selected = null;
        BuildCategories();
        SelectedCategory = Categories.First(x => x.Item == c);
        _storage.RequestSave();
    }

    public void DeleteCategory(CategoryViewModel cvm)
    {
        if (cvm.Item == null) return;
        foreach (var v in _all.Where(v => v.Item.CategoryId == cvm.Id)) v.Item.CategoryId = null;
        _storage.Library.Categories.Remove(cvm.Item);
        _selected = null;
        BuildCategories();
        _storage.RequestSave();
    }

    // ------------------------------------------------------------------ language

    public void SetLanguage(string code)
    {
        Settings.Language = code == "ar" ? "ar" : "en";
        _storage.RequestSave();
        Loc.I.Language = Settings.Language;
    }

    void OnLocChanged()
    {
        foreach (var c in Categories) c.RefreshLoc();
        foreach (var v in _all) v.RefreshLoc();
        Raise(nameof(CableText));
        Raise(nameof(ActiveText));
        Raise(nameof(StopAllHotkeyLabel));
        Raise(nameof(StopAllTooltip));
    }

    public void SaveNow() => _storage.SaveNow();

    public void Dispose()
    {
        _uiTimer.Stop();
        _houseTimer.Stop();
        _statusTimer.Stop();
        _hotkeys.Dispose();
        Engine.Dispose();
        _storage.SaveNow();
    }
}
