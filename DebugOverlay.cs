using System.Drawing;
using System.Windows.Forms;

namespace MouseBridge;

/// <summary>
/// Outlines the corner zones the wrap logic keys off — one square per monitor
/// corner — so an otherwise invisible trigger area can be aimed at directly.
/// </summary>
internal sealed class DebugOverlay : IDisposable
{
    private readonly List<CornerBox> _boxes = [];

    public bool Visible => _boxes.Count > 0;

    /// <summary>Rebuilds the boxes for the given layout; safe to call while already shown.</summary>
    public void Show(Topology topology)
    {
        Hide();

        var zone = topology.CornerZone;
        foreach (var screen in topology.Screens)
        {
            foreach (var corner in Corners(screen, zone))
            {
                var box = new CornerBox(corner);
                _boxes.Add(box);
                box.Show();
            }
        }
    }

    public void Hide()
    {
        foreach (var box in _boxes) box.Dispose();
        _boxes.Clear();
    }

    private static IEnumerable<Rectangle> Corners(Rectangle screen, int zone)
    {
        // Clamped so a monitor narrower than two zones still yields sane boxes.
        var w = Math.Min(zone, screen.Width);
        var h = Math.Min(zone, screen.Height);

        yield return new Rectangle(screen.Left, screen.Top, w, h);
        yield return new Rectangle(screen.Right - w, screen.Top, w, h);
        yield return new Rectangle(screen.Left, screen.Bottom - h, w, h);
        yield return new Rectangle(screen.Right - w, screen.Bottom - h, w, h);
    }

    public void Dispose() => Hide();

    /// <summary>A hollow, click-through window showing one corner zone as a yellow outline.</summary>
    private sealed class CornerBox : Form
    {
        /// <summary>Keyed out to transparent, so only the drawn border is ever visible.</summary>
        private static readonly Color Hollow = Color.Magenta;

        private readonly Rectangle _target;

        public CornerBox(Rectangle target)
        {
            _target = target;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            // The bounds are in the same physical pixels the hook reports;
            // letting WinForms scale them would misplace the outline.
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Hollow;
            TransparencyKey = Hollow;
            Bounds = target;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Pens.Yellow is a shared, cached pen, so repainting allocates nothing.
            e.Graphics.DrawRectangle(Pens.Yellow, 0, 0, Width - 1, Height - 1);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // A scale-factor change makes WinForms resize the window out from
            // under us; the zone is a fixed pixel count, so put it back.
            if (m.Msg == Native.WM_DPICHANGED) Bounds = _target;
        }
    }
}
