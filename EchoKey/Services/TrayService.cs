using System;
using EchoKey.Core;
using EchoKey.ViewModels;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace EchoKey.Services;

/// <summary>Notification-area icon with a small control menu.</summary>
public sealed class TrayService : IDisposable
{
    readonly WinForms.NotifyIcon _icon;
    readonly WinForms.ContextMenuStrip _menu;
    readonly WinForms.ToolStripMenuItem _open, _sb, _hk, _stop, _exit;
    readonly MainViewModel _vm;

    public TrayService(MainViewModel vm, Action showWindow, Action exitApp)
    {
        _vm = vm;

        _menu = new WinForms.ContextMenuStrip
        {
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false },
            BackColor = Drawing.Color.FromArgb(17, 24, 38),
            ForeColor = Drawing.Color.FromArgb(232, 238, 247),
            ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 9.5f)
        };

        _open = new WinForms.ToolStripMenuItem();
        _open.Click += (_, _) => showWindow();
        _sb = new WinForms.ToolStripMenuItem();
        _sb.Click += (_, _) => _vm.SoundboardEnabled = !_vm.SoundboardEnabled;
        _hk = new WinForms.ToolStripMenuItem();
        _hk.Click += (_, _) => _vm.HotkeysEnabled = !_vm.HotkeysEnabled;
        _stop = new WinForms.ToolStripMenuItem();
        _stop.Click += (_, _) => _vm.StopAll();
        _exit = new WinForms.ToolStripMenuItem();
        _exit.Click += (_, _) => exitApp();

        _menu.Items.Add(_open);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(_sb);
        _menu.Items.Add(_hk);
        _menu.Items.Add(_stop);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(_exit);

        Drawing.Icon icon;
        try
        {
            icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? Drawing.SystemIcons.Application;
        }
        catch { icon = Drawing.SystemIcons.Application; }

        _icon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "EchoKey",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) showWindow();
        };

        _vm.PropertyChanged += (_, _) => UpdateState();
        Loc.I.Changed += UpdateTexts;
        UpdateTexts();
    }

    void UpdateTexts()
    {
        var l = Loc.I;
        _menu.RightToLeft = l.IsRtl ? WinForms.RightToLeft.Yes : WinForms.RightToLeft.No;
        _open.Text = l["trayOpen"];
        _stop.Text = l["stopAll"];
        _exit.Text = l["trayExit"];
        _sb.Text = l["soundboard"];
        _hk.Text = l["hotkeys"];
        UpdateState();
    }

    void UpdateState()
    {
        _sb.Checked = _vm.SoundboardEnabled;
        _hk.Checked = _vm.HotkeysEnabled;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    sealed class DarkColors : WinForms.ProfessionalColorTable
    {
        static readonly Drawing.Color Bg = Drawing.Color.FromArgb(17, 24, 38);
        static readonly Drawing.Color Hi = Drawing.Color.FromArgb(15, 46, 64);
        static readonly Drawing.Color Line = Drawing.Color.FromArgb(34, 48, 73);

        public override Drawing.Color ToolStripDropDownBackground => Bg;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color MenuItemBorder => Hi;
        public override Drawing.Color MenuItemSelected => Hi;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hi;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hi;
        public override Drawing.Color MenuItemPressedGradientBegin => Hi;
        public override Drawing.Color MenuItemPressedGradientEnd => Hi;
        public override Drawing.Color ImageMarginGradientBegin => Bg;
        public override Drawing.Color ImageMarginGradientMiddle => Bg;
        public override Drawing.Color ImageMarginGradientEnd => Bg;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Line;
    }
}
