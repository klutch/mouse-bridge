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

    /// <summary>Width and height of the square drawn on each corner.</summary>
    private const int SquareSize = 16;

    /// <summary>Shared by every square, so it lives here rather than on one of them.</summary>
    internal static readonly Font LabelFont = new("Segoe UI", 8f, FontStyle.Bold);

    /// <summary>How far the black copy behind the label is shifted, in pixels.</summary>
    internal const int ShadowOffset = 1;

    /// <summary>Room the label needs, including the pixel the shadow adds.</summary>
    internal static Size MeasureLabel(string label)
    {
        using var dc = Graphics.FromHwnd(IntPtr.Zero);
        return Size.Ceiling(dc.MeasureString(label, LabelFont)) + new Size(ShadowOffset, ShadowOffset);
    }

    private readonly List<CornerBox> _boxes = [];
    private DotWindow? _dot;

    public bool Visible => _dot is not null;

    /// <summary>Rebuilds the overlay for the given layout; safe to call while already shown.</summary>
    public void Show(Topology topology, POINT cursor)
    {
        Hide();

        _dot = new DotWindow(cursor);
        _dot.Show();

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
                    SquareSize,
                    Palette[(m * corners.Length + c) % Palette.Length],
                    $"{m + 1}{CornerNames[c]} ({corners[c].X},{corners[c].Y})",
                    labelOnRight: c is 0 or 2);

                _boxes.Add(box);
                box.Show();
            }
        }
    }

    /// <summary>Puts the dot at the virtual cursor. Called often, so it does no work when nothing moved.</summary>
    public void MoveDot(POINT cursor) => _dot?.MoveTo(cursor);

    public void Hide()
    {
        foreach (var box in _boxes) box.Dispose();
        _boxes.Clear();

        _dot?.Dispose();
        _dot = null;
    }

    public void Dispose() => Hide();

    /// <summary>
    /// Shared setup for every piece of the overlay: sits above other windows,
    /// never takes focus, and passes clicks straight through to what is behind.
    /// </summary>
    private abstract class OverlayWindow : Form
    {
        /// <summary>Keyed out to see-through, so only what is drawn shows.</summary>
        protected static readonly Color Hollow = Color.Magenta;

        protected OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            // Positions are in the same pixels the hook reports; letting
            // WinForms scale them would put everything in the wrong place.
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Hollow;
            TransparencyKey = Hollow;
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
    }

    /// <summary>A click-through window holding one filled square and its label.</summary>
    private sealed class CornerBox : OverlayWindow
    {
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

            var text = MeasureLabel(label);

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

            Bounds = _target;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.FillRectangle(_brush, _square);

            // Hard edged text on purpose: smoothed edges blend into the
            // see-through colour and leave a fringe around every letter.
            e.Graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            // A black copy behind the label, shifted a pixel, so the colours
            // stay readable over a light desktop. Brushes.Black is shared, so
            // this costs nothing to draw.
            e.Graphics.DrawString(
                _label, LabelFont, Brushes.Black, _labelAt.X + ShadowOffset, _labelAt.Y + ShadowOffset);
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

    /// <summary>A green dot on black, marking where the virtual cursor is.</summary>
    private sealed class DotWindow : OverlayWindow
    {
        private const int Radius = 3;

        /// <summary>Black left showing around the green, so it reads on any background.</summary>
        private const int Ring = 1;

        /// <summary>
        /// The window is wider than the dot because Windows will not make a
        /// window of this kind any smaller than 16 by 16. The spare room around
        /// the dot is see-through, so it makes no difference on screen.
        /// </summary>
        private const int Span = 16;

        private static readonly Rectangle Black = Centred(Radius + Ring);
        private static readonly Rectangle Green = Centred(Radius);

        /// <summary>Where the window was last put, so a still mouse costs nothing.</summary>
        private Point _at = new(int.MinValue, int.MinValue);

        public DotWindow(POINT at) => MoveTo(at);

        public void MoveTo(POINT p)
        {
            var at = new Point(p.X - Span / 2, p.Y - Span / 2);
            if (at == _at) return;

            _at = at;
            Bounds = new Rectangle(at.X, at.Y, Span, Span);
        }

        private static Rectangle Centred(int radius) =>
            new(Span / 2 - radius, Span / 2 - radius, radius * 2, radius * 2);

        protected override void OnPaint(PaintEventArgs e)
        {
            // Both circles are drawn with hard edges. Smoothed ones would blend
            // into the see-through colour and leave a ring of fringe behind.
            e.Graphics.FillEllipse(Brushes.Black, Black);
            e.Graphics.FillEllipse(Brushes.Lime, Green);
        }
    }
}
