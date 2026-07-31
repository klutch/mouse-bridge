using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseBridge;

/// <summary>Tray presence: start/stop, logging toggle, and display-change handling.</summary>
internal sealed class TrayApp : IDisposable
{
    /// <summary>
    /// The helper NotifyIcon uses for its own right-click menu. It puts the menu
    /// in front first, which is what makes clicking elsewhere close it again, so
    /// borrowing it gives left click exactly the same behaviour as right click.
    /// </summary>
    private static readonly MethodInfo? ShowTrayMenu = typeof(NotifyIcon).GetMethod(
        "ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

    private readonly Logger _log;
    private readonly Bridge _bridge;
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _verboseItem;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly DebugOverlay _overlay = new();
    private readonly System.Windows.Forms.Timer _dotTimer;
    private readonly Icon _icon;

    public TrayApp(bool verbose)
    {
        _log = new Logger(verbose);
        _bridge = new Bridge(_log);

        _startupItem = new ToolStripMenuItem("Start on login") { Checked = StartupRegistration.IsEnabled() };
        _startupItem.Click += (_, _) => ToggleStartup();

        _verboseItem = new ToolStripMenuItem("Verbose logging") { Checked = verbose };
        _verboseItem.Click += (_, _) =>
        {
            _log.Verbose = !_log.Verbose;
            _verboseItem.Checked = _log.Verbose;
            _log.Write($"verbose = {_log.Verbose}");
        };

        // Only the tick is set here. The squares themselves go up in Run(),
        // once the hook is in and the app is about to start listening.
        _overlayItem = new ToolStripMenuItem("Debug overlay") { Checked = Settings.DebugOverlay };
        _overlayItem.Click += (_, _) => ToggleOverlay();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_verboseItem);
        _menu.Items.Add(_overlayItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Reload display layout", null, (_, _) => Reload()));
        _menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogFolder()));
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
        // rectangles the overlay is pinned to.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // The dot is moved on a timer rather than straight from the hook. The
        // hook has to hand control back quickly or Windows drops it, and moving
        // a window is not work worth doing in there.
        _dotTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _dotTimer.Tick += (_, _) => _overlay.MoveDot(_bridge.VirtualCursor);

        // Nothing in the tooltip changes on its own any more, so it is written
        // here and again whenever the display layout changes.
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
            _log.Write("install failed: " + ex.Message);
            _log.Flush();
            MessageBox.Show(ex.Message, "MouseBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (_overlayItem.Checked)
        {
            ApplyOverlay(true);
            _log.Write("debug overlay = True (saved setting)");
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

        // The boxes are pinned to monitor rectangles that have just changed.
        if (_overlay.Visible) _overlay.Show(_bridge.Topology, _bridge.VirtualCursor);

        UpdateTooltip();
    }

    /// <summary>Puts the overlay up or takes it down, dot and all.</summary>
    private void ApplyOverlay(bool on)
    {
        if (on)
        {
            _overlay.Show(_bridge.Topology, _bridge.VirtualCursor);
            _dotTimer.Start();
        }
        else
        {
            _dotTimer.Stop();
            _overlay.Hide();
        }
    }

    private void ToggleOverlay()
    {
        var on = !_overlayItem.Checked;
        _overlayItem.Checked = on;

        ApplyOverlay(on);

        _log.Write($"debug overlay = {on}");

        // A setting that will not save is worth a line in the log, but not
        // worth interrupting: the overlay itself has already switched.
        try
        {
            Settings.DebugOverlay = on;
        }
        catch (Exception ex)
        {
            _log.Write("could not save debug overlay setting: " + ex.Message);
        }
    }

    private void ToggleStartup()
    {
        var wanted = !_startupItem.Checked;
        try
        {
            StartupRegistration.SetEnabled(wanted);
            _startupItem.Checked = wanted;
            _log.Write($"start on login = {wanted}");
        }
        catch (Exception ex)
        {
            _log.Write("start on login failed: " + ex.Message);
            _startupItem.Checked = StartupRegistration.IsEnabled();
            MessageBox.Show(ex.Message, "MouseBridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateTooltip() =>
        // NotifyIcon.Text is capped at 63 characters.
        _tray.Text = $"MouseBridge — {_bridge.Topology.Screens.Count} displays";

    private void OpenLogFolder()
    {
        _log.Flush();
        Process.Start(new ProcessStartInfo(_log.Folder) { UseShellExecute = true });
    }

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
        _dotTimer.Dispose();
        _overlay.Dispose();
        _tray.MouseUp -= OnTrayMouseUp;
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _bridge.Dispose();
        _log.Dispose();
        _icon.Dispose();
    }
}
