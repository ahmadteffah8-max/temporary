using System;
using System.Windows;

namespace EchoKey.Views;

public partial class InputDialog : Window
{
    public string? Value { get; private set; }

    public InputDialog(Window? owner, string title, string prompt, string initial = "")
    {
        InitializeComponent();
        if (owner != null && owner.IsVisible) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initial;
        SourceInitialized += (_, _) => Dialogs.UseDarkTitleBar(this);
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        string v = InputBox.Text.Trim();
        if (v.Length == 0) return;
        Value = v;
        DialogResult = true;
    }

    public static string? Ask(Window? owner, string title, string prompt, string initial = "")
    {
        var dlg = new InputDialog(owner, title, prompt, initial);
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
