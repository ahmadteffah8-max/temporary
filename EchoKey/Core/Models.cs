using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace EchoKey.Core;

public sealed class HotkeyBinding
{
    public const int ModAlt = 0x1;
    public const int ModControl = 0x2;
    public const int ModShift = 0x4;
    public const int ModWin = 0x8;

    public int Modifiers { get; set; }
    public int VirtualKey { get; set; }

    public bool SameAs(HotkeyBinding? other) =>
        other != null && other.Modifiers == Modifiers && other.VirtualKey == VirtualKey;

    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join(" + ", parts);
    }

    public static string KeyName(int vk)
    {
        Key k = KeyInterop.KeyFromVirtualKey(vk);
        if (k >= Key.D0 && k <= Key.D9) return ((int)k - (int)Key.D0).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Num " + ((int)k - (int)Key.NumPad0);
        switch (k)
        {
            case Key.OemTilde: return "~";
            case Key.OemMinus: return "-";
            case Key.OemPlus: return "=";
            case Key.OemComma: return ",";
            case Key.OemPeriod: return ".";
            case Key.OemQuestion: return "/";
            case Key.OemSemicolon: return ";";
            case Key.OemQuotes: return "'";
            case Key.OemOpenBrackets: return "[";
            case Key.OemCloseBrackets: return "]";
            case Key.OemPipe: return "\\";
            case Key.Return: return "Enter";
            case Key.Back: return "Backspace";
            case Key.Prior: return "PageUp";
            case Key.Next: return "PageDown";
            case Key.Capital: return "CapsLock";
            case Key.Multiply: return "Num *";
            case Key.Add: return "Num +";
            case Key.Subtract: return "Num -";
            case Key.Divide: return "Num /";
            case Key.Decimal: return "Num .";
            default: return k.ToString();
        }
    }
}

public sealed class SoundItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>File name inside the app's Sounds folder (a private copy of the imported file).</summary>
    public string FileName { get; set; } = "";
    public double DurationSeconds { get; set; }
    public float Volume { get; set; } = 1f;
    public Guid? CategoryId { get; set; }
    public int Order { get; set; }
    public HotkeyBinding? Hotkey { get; set; }
}

public sealed class CategoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int Order { get; set; }
}

public sealed class LibraryData
{
    public List<CategoryItem> Categories { get; set; } = new();
    public List<SoundItem> Sounds { get; set; } = new();
}

public sealed class AppSettings
{
    public string? Language { get; set; }
    /// <summary>"default" = system default capture device, "none" = no real microphone, otherwise a device id.</summary>
    public string? MicDeviceId { get; set; } = "default";
    /// <summary>"auto" = VB-CABLE (CABLE Input), otherwise a render device id.</summary>
    public string? OutputDeviceId { get; set; } = "auto";
    public bool CableHelpShown { get; set; }
    public float MicVolume { get; set; } = 1f;
    public float SoundboardVolume { get; set; } = 1f;
    public bool MicEnabled { get; set; } = true;
    public bool SoundboardEnabled { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    /// <summary>WASAPI shared-mode buffer in milliseconds. Raise it (e.g. 60-80) if you hear crackling.</summary>
    public int OutputLatencyMs { get; set; } = 40;
}
