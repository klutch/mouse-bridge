using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseBridge;

/// <summary>Tray presence: start on login, and display-change handling.</summary>
internal sealed class TrayApp : IDisposable
{
    /// <summary>
    /// The helper NotifyIcon uses for its own right-click menu. It puts the menu
    /// in front first, which is what makes clicking elsewhere close it again, so
    /// borrowing it gives left click exactly the same behaviour as right click.
    /// </summary>
    private static readonly MethodInfo? ShowTrayMenu = typeof(NotifyIcon).GetMethod(
        "ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

    private readonly Bridge _bridge;
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Icon _icon;

    public TrayApp()
    {
        _bridge = new Bridge();

        _startupItem = new ToolStripMenuItem("Start on login") { Checked = StartupRegistration.IsEnabled() };
        _startupItem.Click += (_, _) => ToggleStartup();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Reload display layout", null, (_, _) => Reload()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.ExitThread()));

        _icon = LoadIcon();
        _tray = new NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = _menu,
            Visible = true,
            Text = "MouseBridge",
        };

        // Right click opens the menu on its own; this adds left click.
        _tray.MouseUp += OnTrayMouseUp;

        // Monitors being added, removed, or rearranged invalidates the cached
        // rectangles the bridge clamps against.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Nothing in the tooltip changes on its own, so it is written here and
        // again whenever the display layout changes.
        UpdateTooltip();
    }

    public void Run()
    {
        try
        {
            _bridge.Install();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "MouseBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run();
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (ShowTrayMenu is not null)
        {
            ShowTrayMenu.Invoke(_tray, null);
            return;
        }

        // Should the helper ever disappear, still show something. The menu may
        // then need a second click elsewhere to close.
        _menu.Show(Control.MousePosition);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        _bridge.RefreshTopology();
        UpdateTooltip();
    }

    private void ToggleStartup()
    {
        var wanted = !_startupItem.Checked;
        try
        {
            StartupRegistration.SetEnabled(wanted);
            _startupItem.Checked = wanted;
        }
        catch (Exception ex)
        {
            _startupItem.Checked = StartupRegistration.IsEnabled();
            MessageBox.Show(ex.Message, "MouseBridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateTooltip() =>
        // NotifyIcon.Text is capped at 63 characters.
        _tray.Text = $"MouseBridge — {_bridge.Topology.Screens.Count} displays";

    /// <summary>
    /// The app icon: two offset panels with an arrow bridging the step between
    /// them. It comes from the same .ico the executable uses, picked at whatever
    /// size the tray wants so it stays sharp on high-DPI displays.
    /// </summary>
    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("MouseBridge.ico")
            ?? throw new InvalidOperationException("MouseBridge.ico is missing from the assembly.");

        var wanted = SystemInformation.SmallIconSize;
        return new Icon(stream, wanted.Width, wanted.Height);
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _tray.MouseUp -= OnTrayMouseUp;
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _bridge.Dispose();
        _icon.Dispose();
    }
}
