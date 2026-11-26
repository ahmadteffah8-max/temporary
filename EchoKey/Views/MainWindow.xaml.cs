using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EchoKey.Audio;
using EchoKey.Core;
using EchoKey.ViewModels;
using Microsoft.Win32;

namespace EchoKey.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        SourceInitialized += (_, _) => Dialogs.UseDarkTitleBar(this);
        IsVisibleChanged += (_, _) => _vm.WindowVisible = IsVisible;
        ContentRendered += OnFirstRender;
    }

    bool _helpChecked;
    void OnFirstRender(object? sender, EventArgs e)
    {
        if (_helpChecked) return;
        _helpChecked = true;
        // First run without VB-CABLE: show the install instructions once.
        if (!_vm.CableFound && !_vm.Settings.CableHelpShown)
        {
            _vm.Settings.CableHelpShown = true;
            _vm.SaveNow();
            ShowCableHelp();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_vm.Exiting && _vm.Settings.CloseToTray)
        {
            e.Cancel = true; // keep running in the tray
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    // ------------------------------------------------------------ helpers

    static T? Ctx<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

    static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        mi.Click += (_, _) => action();
        return mi;
    }

    ContextMenu NewMenu() => new() { FlowDirection = Loc.I.FlowDirection };

    void ShowCableHelp() =>
        Dialogs.Info(this, Loc.I["cableHelp"], Loc.I["cableHelpTitle"]);

    // ------------------------------------------------------------ drag & drop import

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            await _vm.ImportAsync(files);
    }

    async void AddSounds_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Audio (MP3, WAV, OGG, OGA, FLAC)|*.mp3;*.wav;*.ogg;*.oga;*.flac|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            await _vm.ImportAsync(dlg.FileNames);
    }

    // ------------------------------------------------------------ header

    void StopAll_Click(object sender, RoutedEventArgs e) => _vm.StopAll();

    void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(_vm, this).ShowDialog();

    void Cable_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshCable();
        if (!_vm.CableFound) ShowCableHelp();
    }

    // ------------------------------------------------------------ cards

    void Play_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<SoundViewModel>(sender) is { } vm) _vm.PlaySound(vm);
    }

    void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<SoundViewModel>(sender) is { } vm) _vm.PreviewSound(vm);
    }

    void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<SoundViewModel>(sender) is { } vm) EditHotkey(vm);
    }

    void EditHotkey(SoundViewModel vm)
    {
        var dlg = new HotkeyDialog(this, vm.Name, vm.Item.Hotkey);
        if (dlg.ShowDialog() == true)
            _vm.SetHotkey(vm, dlg.Result);
    }

    void More_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<SoundViewModel>(sender) is { } vm && sender is FrameworkElement fe)
            ShowSoundMenu(vm, fe);
    }

    void Card_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (Ctx<SoundViewModel>(sender) is { } vm && sender is FrameworkElement fe)
        {
            ShowSoundMenu(vm, fe);
            e.Handled = true;
        }
    }

    void ShowSoundMenu(SoundViewModel vm, FrameworkElement target)
    {
        var l = Loc.I;
        var menu = NewMenu();

        menu.Items.Add(Item(l["rename"], () =>
        {
            string? name = InputDialog.Ask(this, "EchoKey", l["soundName"], vm.Name);
            if (name != null) vm.Name = name;
        }));
        menu.Items.Add(Item(vm.HasHotkey ? l["changeHotkey"] : l["setHotkey"], () => EditHotkey(vm)));
        if (vm.HasHotkey)
            menu.Items.Add(Item(l["removeHotkey"], () => _vm.SetHotkey(vm, null)));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(l["moveEarlier"], () => _vm.MoveSound(vm, -1), _vm.VisibleSounds.IndexOf(vm) > 0));
        menu.Items.Add(Item(l["moveLater"], () => _vm.MoveSound(vm, 1), _vm.VisibleSounds.IndexOf(vm) < _vm.VisibleSounds.Count - 1));

        // categories ("folders")
        menu.Items.Add(new Separator());
        foreach (var c in _vm.Categories)
        {
            if (c.Kind == CategoryKind.All) continue;
            Guid? target_id = c.Id;
            bool current = vm.Item.CategoryId == target_id;
            string text = (current ? "✓  " : "") + l.Format("moveTo", c.DisplayName);
            menu.Items.Add(Item(text, () => _vm.MoveToCategory(vm, target_id), !current));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(l["delete"], () =>
        {
            if (Dialogs.Confirm(this, l.Format("confirmDelete", vm.Name), "EchoKey"))
                _vm.DeleteSound(vm);
        }));

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------ categories

    void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        string? name = InputDialog.Ask(this, "EchoKey", Loc.I["categoryName"]);
        if (name != null) _vm.AddCategory(name);
    }

    void Category_RightClick(object sender, MouseButtonEventArgs e)
    {
        // find the ListBoxItem under the cursor
        DependencyObject? d = e.OriginalSource as DependencyObject;
        while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
        if (d is not ListBoxItem lbi || lbi.DataContext is not CategoryViewModel cvm || !cvm.IsUser) return;

        var l = Loc.I;
        var menu = NewMenu();
        menu.Items.Add(Item(l["rename"], () =>
        {
            string? name = InputDialog.Ask(this, "EchoKey", l["categoryName"], cvm.DisplayName);
            if (name != null) cvm.Rename(name);
        }));
        menu.Items.Add(Item(l["delete"], () =>
        {
            if (Dialogs.Confirm(this, l.Format("confirmDeleteCategory", cvm.DisplayName), "EchoKey"))
                _vm.DeleteCategory(cvm);
        }));
        menu.PlacementTarget = lbi;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
