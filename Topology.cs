using System.Drawing;

namespace MouseBridge;

/// <summary>
/// A snapshot of where the monitors are.
/// </summary>
/// <remarks>
/// Nothing here talks to Windows except the one call that reads the screen
/// list, so it can be built from a made up layout and checked without a real
/// multi-monitor rig.
/// </remarks>
internal sealed class Topology
{
    private readonly Rectangle[] _screens;

    public Topology(Rectangle[] screens) => _screens = screens;

    public static Topology FromSystem() =>
        new(System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray());

    public IReadOnlyList<Rectangle> Screens => _screens;

    /// <summary>True when the point falls on one of the monitors.</summary>
    public bool Contains(POINT p)
    {
        foreach (var s in _screens)
            if (s.Contains(p.X, p.Y)) return true;
        return false;
    }

    /// <summary>
    /// The nearest point that sits on a monitor. A point already on one comes
    /// back unchanged, and with no monitors at all the point is left alone.
    /// </summary>
    public POINT Clamp(POINT p)
    {
        var best = p;
        var bestDistance = long.MaxValue;

        foreach (var s in _screens)
        {
            if (s.Width <= 0 || s.Height <= 0) continue;

            // The nearest point on a monitor is the point with each axis pulled
            // into range on its own. Right and Bottom sit one past the last
            // pixel, which is why they lose one here.
            var x = Math.Clamp(p.X, s.Left, s.Right - 1);
            var y = Math.Clamp(p.Y, s.Top, s.Bottom - 1);

            long dx = p.X - x;
            long dy = p.Y - y;
            var distance = dx * dx + dy * dy;

            // Nothing moved, so the point was already on this monitor.
            if (distance == 0) return p;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new POINT(x, y);
            }
        }

        return best;
    }

    public string Describe() =>
        string.Join(Environment.NewLine, _screens.Select((s, i) =>
            $"  [{i}] x={s.X} y={s.Y} w={s.Width} h={s.Height}"));
}
