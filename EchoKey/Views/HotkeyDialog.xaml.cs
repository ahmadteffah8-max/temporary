using System.Windows;
using System.Windows.Input;
using EchoKey.Core;

namespace EchoKey.Views;

/// <summary>Captures a key combination (e.g. F2, Ctrl+F1, Alt+Q). Result == null with DialogResult true means "remove".</summary>
public partial class HotkeyDialog : Window
{
    HotkeyBinding? _captured;

    public HotkeyBinding? Result { get; private set; }

    public HotkeyDialog(Window? owner, string soundName, HotkeyBinding? current)
    {
        InitializeComponent();
        if (owner != null && owner.IsVisible) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "EchoKey";
        TitleText.Text = Loc.I.Format("hotkeyFor", soundName);
        ComboText.Text = current?.ToString() ?? "—";
        RemoveButton.IsEnabled = current != null;
        SourceInitialized += (_, _) => Dialogs.UseDarkTitleBar(this);
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };

        // Plain Escape closes the dialog (Cancel button).
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) return;

        e.Handled = true;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return; // wait for the real key

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        _captured = new HotkeyBinding { Modifiers = (int)Keyboard.Modifiers, VirtualKey = vk };
        ComboText.Text = _captured.ToString();
        SaveButton.IsEnabled = true;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_captured == null) return;
        Result = _captured;
        DialogResult = true;
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = true;
    }
}
