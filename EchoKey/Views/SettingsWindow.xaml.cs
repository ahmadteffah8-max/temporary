using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EchoKey.Audio;
using EchoKey.Core;
using EchoKey.Services;
using EchoKey.ViewModels;

namespace EchoKey.Views;

public partial class SettingsWindow : Window
{
    readonly MainViewModel _vm;

    public SettingsWindow(MainViewModel vm, Window? owner)
    {
        InitializeComponent();
        _vm = vm;
        if (owner != null && owner.IsVisible) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SourceInitialized += (_, _) => Dialogs.UseDarkTitleBar(this);

        LangBox.SelectedIndex = Loc.I.Language == "ar" ? 1 : 0;
        LatencySlider.Value = vm.Settings.OutputLatencyMs;
        UpdateLatencyText();
        StartupBox.IsChecked = StartupService.IsEnabled;
        TrayBox.IsChecked = vm.Settings.CloseToTray;
        PopulateDevices();
    }

    void PopulateDevices()
    {
        var mics = new List<DeviceInfo>
        {
            new("default", Loc.I["defaultDevice"]),
            new("none", Loc.I["noMic"])
        };
        mics.AddRange(AudioEngine.GetCaptureDevices());

        var outs = new List<DeviceInfo> { new("auto", Loc.I["autoOutput"]) };
        outs.AddRange(AudioEngine.GetRenderDevices());

        MicBox.ItemsSource = mics;
        OutBox.ItemsSource = outs;
        MicBox.SelectedValue = Pick(mics, _vm.Settings.MicDeviceId, "default");
        OutBox.SelectedValue = Pick(outs, _vm.Settings.OutputDeviceId, "auto");
    }

    static string Pick(List<DeviceInfo> list, string? wanted, string fallback) =>
        wanted != null && list.Any(d => d.Id == wanted) ? wanted : fallback;

    void Refresh_Click(object sender, RoutedEventArgs e)
    {
        // keep the user's current (unsaved) choice if it still exists
        string? mic = MicBox.SelectedValue as string;
        string? outp = OutBox.SelectedValue as string;
        PopulateDevices();
        if (mic != null && ((List<DeviceInfo>)MicBox.ItemsSource).Any(d => d.Id == mic)) MicBox.SelectedValue = mic;
        if (outp != null && ((List<DeviceInfo>)OutBox.ItemsSource).Any(d => d.Id == outp)) OutBox.SelectedValue = outp;
    }

    void Latency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateLatencyText();

    void UpdateLatencyText()
    {
        if (LatencyText == null || LatencySlider == null) return;
        LatencyText.Text = Loc.I.Format("latency", (int)LatencySlider.Value);
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _vm.Settings.CloseToTray = TrayBox.IsChecked == true;
        StartupService.SetEnabled(StartupBox.IsChecked == true);
        _vm.SetLanguage(LangBox.SelectedIndex == 1 ? "ar" : "en");
        _vm.ApplyAudioSettings(
            MicBox.SelectedValue as string ?? "default",
            OutBox.SelectedValue as string ?? "auto",
            (int)LatencySlider.Value);
        DialogResult = true;
    }
}
