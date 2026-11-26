using System;
using Microsoft.Win32;

namespace EchoKey.Services;

/// <summary>"Start with Windows" through HKCU\...\Run (no admin rights, no scheduled task, no service).</summary>
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "EchoKey";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return k?.GetValue(ValueName) is string s && s.Length > 0;
            }
            catch { return false; }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (k == null) return;
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? "";
                if (exe.Length > 0) k.SetValue(ValueName, "\"" + exe + "\" --tray");
            }
            else
            {
                k.DeleteValue(ValueName, false);
            }
        }
        catch { /* registry blocked by policy: ignore */ }
    }
}
