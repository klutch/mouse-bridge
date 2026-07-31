using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MouseBridge;

/// <summary>
/// Marks every monitor corner with a small filled square sitting on top of the
/// corner point, labelled with the monitor number and which corner it is.
/// </summary>
/// <remarks>
/// The square is centred on the corner, so half of it hangs off the monitor.
/// That marks the exact point rather than the area the wrap logic watches.
/// </remarks>
internal sealed class DebugOverlay : IDisposable
{
    /// <summary>
    /// One colour per square, so any two can be told apart at a glance. Nothing
    /// here may be magenta — that colour is what makes the rest of the window
    /// see-through.
    /// </summary>
    private static readonly Color[] Palette =
    [
        Color.FromArgb(230, 60, 50),
        Color.FromArgb(255, 145, 0),
        Color.FromArgb(240, 215, 0),
        Color.FromArgb(70, 200, 90),
        Color.FromArgb(0, 200, 220),
        Color.FromArgb(70, 135, 255),
        Color.FromArgb(175, 110, 255),
        Color.FromArgb(245, 245, 245),
    ];

    private static readonly string[] CornerNames = ["TL", "TR", "BL", "BR"];

    /// <summary>Shared by every square, so it lives here rather than on one of them.</summary>
    internal static readonly Font LabelFont = new("Segoe UI", 8f, FontStyle.Bold);

    private readonly List<CornerBox> _boxes = [];

    public bool Visible => _boxes.Count > 0;

    /// <summary>Rebuilds the squares for the given layout; safe to call while already shown.</summary>
    public void Show(Topology topology)
    {
        Hide();

        var size = topology.CornerZone;

        for (var m = 0; m < topology.Screens.Count; m++)
        {
            var s = topology.Screens[m];

            // Right and bottom use the last pixel inside the monitor, which is
            // the coordinate the jump logic compares against.
            var corners = new[]
            {
                new Point(s.Left, s.Top),
                new Point(s.Right - 1, s.Top),
                new Point(s.Left, s.Bottom - 1),
                new Point(s.Right - 1, s.Bottom - 1),
            };

            for (var c = 0; c < corners.Length; c++)
            {
                var box = new CornerBox(
                    s,
                    corners[c],
                    size,
                    Palette[(m * corners.Length + c) % Palette.Length],
                    $"{m + 1}{CornerNames[c]}",
                    labelOnRight: c is 0 or 2);

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

    public void Dispose() => Hide();

    /// <summary>A click-through window holding one filled square and its label.</summary>
    private sealed class CornerBox : Form
    {
        /// <summary>Keyed out to see-through, so only the square and label show.</summary>
        private static readonly Color Hollow = Color.Magenta;

        /// <summary>Space between the square and its label.</summary>
        private const int Gap = 3;

        private readonly SolidBrush _brush;
        private readonly Rectangle _square;
        private readonly PointF _labelAt;
        private readonly string _label;
        private readonly Rectangle _target;

        public CornerBox(Rectangle screen, Point corner, int size, Color color, string label, bool labelOnRight)
        {
            _label = label;
            _brush = new SolidBrush(color);

            Size text;
            using (var dc = Graphics.FromHwnd(IntPtr.Zero))
                text = Size.Ceiling(dc.MeasureString(label, LabelFont));

            // The square straddles the corner point, half of it off the monitor.
            var square = new Rectangle(corner.X - size / 2, corner.Y - size / 2, size, size);

            // The label goes on whichever side keeps it over the monitor, level
            // with the square. Level would hang off at a top or bottom corner,
            // so it is pulled back to sit fully on the monitor.
            var labelX = labelOnRight ? square.Right + Gap : square.Left - Gap - text.Width;
            var labelY = Math.Clamp(corner.Y - text.Height / 2, screen.Top, screen.Bottom - text.Height);
            var labelRect = new Rectangle(labelX, labelY, text.Width, text.Height);

            // The window has to cover both, and they no longer overlap in a row.
            _target = Rectangle.Union(square, labelRect);

            _square = new Rectangle(square.X - _target.X, square.Y - _target.Y, size, size);
            _labelAt = new PointF(labelRect.X - _target.X, labelRect.Y - _target.Y);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            // The bounds are in the same pixels the hook reports; letting
            // WinForms scale them would put the square in the wrong place.
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Hollow;
            TransparencyKey = Hollow;
            Bounds = _target;
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
            e.Graphics.FillRectangle(_brush, _square);

            // Hard edged text on purpose: smoothed edges blend into the
            // see-through colour and leave a fringe around every letter.
            e.Graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            e.Graphics.DrawString(_label, LabelFont, _brush, _labelAt);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // A scale change makes WinForms resize the window out from under
            // us; the square is a fixed pixel count, so put it back.
            if (m.Msg == Native.WM_DPICHANGED) Bounds = _target;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _brush.Dispose();
            base.Dispose(disposing);
        }
    }
}
