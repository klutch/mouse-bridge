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

    public POINT Clamp(POINT p)
    {
        return p;
        // TODO -- Implement me
    }

    public string Describe() =>
        string.Join(Environment.NewLine, _screens.Select((s, i) =>
            $"  [{i}] x={s.X} y={s.Y} w={s.Width} h={s.Height}"));
}
