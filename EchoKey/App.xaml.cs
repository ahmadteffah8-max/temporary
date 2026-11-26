using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using EchoKey.Core;
using EchoKey.Services;
using EchoKey.ViewModels;
using EchoKey.Views;

namespace EchoKey;

public partial class App : Application
{
    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    MainViewModel? _vm;
    MainWindow? _window;
    TrayService? _tray;
    bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "EchoKey.SingleInstance.Mutex", out bool created);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "EchoKey.ShowWindow.Event");
        if (!created)
        {
            _showEvent.Set(); // ask the running instance to show its window
            Shutdown();
            return;
        }

        ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)), null, -1, false);

        DispatcherUnhandledException += OnUnhandled;
        SessionEnding += (_, _) => ExitApp();

        AppPaths.EnsureCreated();
        _vm = new MainViewModel();
        _vm.Initialize();
        _window = new MainWindow(_vm);
        _tray = new TrayService(_vm, ShowMainWindow, ExitApp);

        if (!e.Args.Contains("--tray"))
            ShowMainWindow();
    }

    public void ShowMainWindow()
    {
        if (_window == null || _exiting) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        if (_vm != null) _vm.Exiting = true;
        _tray?.Dispose();
        _vm?.Dispose();
        _window?.Close();
        Shutdown();
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep running for hours: log and continue instead of crashing the stream.
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.Root, "error.log"),
                DateTime.Now.ToString("u") + "\n" + e.Exception + "\n\n");
        }
        catch { }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _showEvent?.Dispose();
        base.OnExit(e);
    }
}
