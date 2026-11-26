using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EchoKey.Core;

namespace EchoKey.Views;

public static class Dialogs
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar on Windows 10 (2004+) and Windows 11. Silently ignored if unsupported.</summary>
    public static void UseDarkTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch { }
    }

    static MessageBoxOptions Opts => Loc.I.IsRtl ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading : MessageBoxOptions.None;

    public static void Info(Window? owner, string text, string title)
    {
        if (owner != null && owner.IsVisible)
            MessageBox.Show(owner, text, title, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, Opts);
        else
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, Opts);
    }

    public static bool Confirm(Window? owner, string text, string title)
    {
        MessageBoxResult r;
        if (owner != null && owner.IsVisible)
            r = MessageBox.Show(owner, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, Opts);
        else
            r = MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, Opts);
        return r == MessageBoxResult.Yes;
    }
}
