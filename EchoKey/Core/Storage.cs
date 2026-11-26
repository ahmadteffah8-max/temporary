using System;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace EchoKey.Core;

public static class AppPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EchoKey");
    public static readonly string SoundsDir = Path.Combine(Root, "Sounds");
    public static readonly string SettingsFile = Path.Combine(Root, "settings.json");
    public static readonly string LibraryFile = Path.Combine(Root, "library.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SoundsDir);
    }
}

/// <summary>Loads and saves settings + library as JSON (atomic writes, debounced).</summary>
public sealed class Storage
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    readonly DispatcherTimer _debounce;

    public AppSettings Settings { get; private set; } = new();
    public LibraryData Library { get; private set; } = new();

    public Storage()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); SaveNow(); };
    }

    public void Load()
    {
        AppPaths.EnsureCreated();
        Settings = LoadFile<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
        Library = LoadFile<LibraryData>(AppPaths.LibraryFile) ?? new LibraryData();
    }

    static T? LoadFile<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts);
        }
        catch
        {
            // Corrupt file: keep a backup and start fresh instead of crashing.
            try { File.Copy(path, path + ".bak", true); } catch { /* ignore */ }
            return null;
        }
    }

    public void RequestSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void SaveNow()
    {
        _debounce.Stop();
        try
        {
            AppPaths.EnsureCreated();
            WriteAtomic(AppPaths.SettingsFile, JsonSerializer.Serialize(Settings, JsonOpts));
            WriteAtomic(AppPaths.LibraryFile, JsonSerializer.Serialize(Library, JsonOpts));
        }
        catch
        {
            // Saving must never crash the app (disk full, antivirus lock, ...).
        }
    }

    static void WriteAtomic(string path, string content)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, true);
    }
}
