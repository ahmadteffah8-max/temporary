using System;
using System.Globalization;
using EchoKey.Core;

namespace EchoKey.ViewModels;

public sealed class SoundViewModel : ObservableObject
{
    readonly Action _changed;
    int _active;
    bool _hotkeyFailed;

    public SoundItem Item { get; }

    public SoundViewModel(SoundItem item, Action changed)
    {
        Item = item;
        _changed = changed;
    }

    public string Name
    {
        get => Item.Name;
        set
        {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value == Item.Name) return;
            Item.Name = value;
            Raise();
            _changed();
        }
    }

    public string DurationText
    {
        get
        {
            var t = TimeSpan.FromSeconds(Item.DurationSeconds);
            if (t.TotalHours >= 1) return t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
            if (t.TotalSeconds < 10) return t.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            return t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>0 … 2 (200 %).</summary>
    public double Volume
    {
        get => Item.Volume;
        set
        {
            float v = (float)Math.Clamp(value, 0, 2);
            if (Math.Abs(v - Item.Volume) < 0.0001f) return;
            Item.Volume = v;
            Raise();
            Raise(nameof(VolumeText));
            _changed();
        }
    }

    public string VolumeText => ((int)Math.Round(Item.Volume * 100)).ToString(CultureInfo.InvariantCulture) + "%";

    public bool HasHotkey => Item.Hotkey != null;
    public string HotkeyText => Item.Hotkey?.ToString() ?? "";
    public string HotkeyLabel => Item.Hotkey != null ? Item.Hotkey.ToString() : "＋ " + Loc.I["setHotkey"];

    public bool HotkeyFailed
    {
        get => _hotkeyFailed;
        set => Set(ref _hotkeyFailed, value);
    }

    public int ActiveCount
    {
        get => _active;
        set
        {
            if (Set(ref _active, value)) Raise(nameof(IsPlaying));
        }
    }

    public bool IsPlaying => _active > 0;

    public void RefreshHotkey()
    {
        Raise(nameof(HasHotkey));
        Raise(nameof(HotkeyText));
        Raise(nameof(HotkeyLabel));
    }

    public void RefreshLoc() => Raise(nameof(HotkeyLabel));
}

public enum CategoryKind { All, Uncategorized, User }

public sealed class CategoryViewModel : ObservableObject
{
    readonly Action _changed;

    public CategoryKind Kind { get; }
    public CategoryItem? Item { get; }
    public Guid? Id => Item?.Id;
    public bool IsUser => Kind == CategoryKind.User;

    public CategoryViewModel(CategoryKind kind, CategoryItem? item, Action changed)
    {
        Kind = kind;
        Item = item;
        _changed = changed;
    }

    public string DisplayName => Kind switch
    {
        CategoryKind.All => Loc.I["all"],
        CategoryKind.Uncategorized => Loc.I["uncategorized"],
        _ => Item?.Name ?? ""
    };

    public void Rename(string name)
    {
        name = (name ?? "").Trim();
        if (Item == null || name.Length == 0 || name == Item.Name) return;
        Item.Name = name;
        Raise(nameof(DisplayName));
        _changed();
    }

    public void RefreshLoc() => Raise(nameof(DisplayName));
}
