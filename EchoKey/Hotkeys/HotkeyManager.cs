using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using EchoKey.Core;

namespace EchoKey.Hotkeys;

/// <summary>
/// System-wide hotkeys through RegisterHotKey on a message-only window.
/// Works while a game or any other window has focus (as long as that program is not running elevated
/// while EchoKey is not). MOD_NOREPEAT: holding the key fires once; every new press fires again (spam).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly HwndSource _source;
    readonly Dictionary<int, Action> _handlers = new();
    int _nextId = 0x100;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("EchoKeyHotkeys")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Returns the registration id, or 0 when the system refused (already used by another program).</summary>
    public int Register(HotkeyBinding binding, Action callback)
    {
        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, (uint)binding.Modifiers | MOD_NOREPEAT, (uint)binding.VirtualKey))
            return 0;
        _handlers[id] = callback;
        return id;
    }

    public void UnregisterAll()
    {
        foreach (int id in _handlers.Keys)
            UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var cb))
        {
            handled = true;
            try { cb(); } catch { /* never let a callback kill the message loop */ }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
