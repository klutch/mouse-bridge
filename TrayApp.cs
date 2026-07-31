using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseBridge;

/// <summary>Tray presence: start/stop, logging toggle, and display-change handling.</summary>
internal sealed class TrayApp : IDisposable
{
    private readonly Logger _log;
    private readonly Bridge _bridge;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _verboseItem;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly DebugOverlay _overlay = new();
    private readonly System.Windows.Forms.Timer _tooltipTimer;
    private IntPtr _iconHandle;

    public TrayApp(bool verbose)
    {
        _log = new Logger(verbose);
        _bridge = new Bridge(_log);

        _enabledItem = new ToolStripMenuItem("Enabled") { Checked = true };
        _enabledItem.Click += (_, _) =>
        {
            _bridge.Enabled = !_bridge.Enabled;
            _enabledItem.Checked = _bridge.Enabled;
            _log.Write($"enabled = {_bridge.Enabled}");
            UpdateTooltip();
        };

        _startupItem = new ToolStripMenuItem("Start on login") { Checked = StartupRegistration.IsEnabled() };
        _startupItem.Click += (_, _) => ToggleStartup();

        _verboseItem = new ToolStripMenuItem("Verbose logging") { Checked = verbose };
        _verboseItem.Click += (_, _) =>
        {
            _log.Verbose = !_log.Verbose;
            _verboseItem.Checked = _log.Verbose;
            _log.Write($"verbose = {_log.Verbose}");
        };

        _overlayItem = new ToolStripMenuItem("Debug overlay");
        _overlayItem.Click += (_, _) =>
        {
            _overlayItem.Checked = !_overlayItem.Checked;
            if (_overlayItem.Checked) _overlay.Show(_bridge.Topology);
            else _overlay.Hide();
            _log.Write($"debug overlay = {_overlayItem.Checked}");
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_verboseItem);
        menu.Items.Add(_overlayItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Reload display layout", null, (_, _) => Reload()));
        menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.ExitThread()));

        _iconHandle = IntPtr.Zero;
        _tray = new NotifyIcon
        {
            Icon = BuildIcon(out _iconHandle),
            ContextMenuStrip = menu,
            Visible = true,
            Text = "MouseBridge",
        };

        // Monitors being added, removed, or rearranged invalidates the cached
        // rectangles the jump logic is built on.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _tooltipTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
        _tooltipTimer.Start();
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

        Application.Run();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        _bridge.RefreshTopology();

        // The boxes are pinned to monitor rectangles that have just changed.
        if (_overlay.Visible) _overlay.Show(_bridge.Topology);

        UpdateTooltip();
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

    private void UpdateTooltip()
    {
        var state = _bridge.Enabled ? "on" : "paused";
        // NotifyIcon.Text is capped at 63 characters.
        _tray.Text = $"MouseBridge ({state}) — {_bridge.Topology.Screens.Count} displays, {_bridge.JumpCount} jumps";
    }

    private void OpenLogFolder()
    {
        _log.Flush();
        Process.Start(new ProcessStartInfo(_log.Folder) { UseShellExecute = true });
    }

    /// <summary>Two offset panels with an arrow bridging the step between them.</summary>
    private static Icon BuildIcon(out IntPtr handle)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var panel = new Pen(Color.FromArgb(235, 235, 235), 2f);
            using var arrow = new Pen(Color.FromArgb(90, 200, 250), 2.5f)
            {
                EndCap = LineCap.ArrowAnchor,
            };

            g.DrawRectangle(panel, 3, 9, 10, 16);   // tall panel, sitting low
            g.DrawRectangle(panel, 19, 4, 10, 11);  // wide panel, sitting high
            g.DrawLine(arrow, 9, 20, 24, 20);
        }

        handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _tooltipTimer.Dispose();
        _overlay.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _bridge.Dispose();
        _log.Dispose();

        if (_iconHandle != IntPtr.Zero)
        {
            Native.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }
}
