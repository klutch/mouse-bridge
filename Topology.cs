using System.Drawing;

namespace MouseBridge;

internal enum Direction { Left, Right, Up, Down }

/// <summary>
/// A snapshot of the monitor rectangles plus the rule that decides where a
/// cursor escaping the desktop should be teleported to.
/// </summary>
/// <remarks>
/// Pure logic on purpose — no P/Invoke, no Windows Forms — so the decision can
/// be exercised against synthetic layouts without a real multi-monitor rig.
/// </remarks>
internal sealed class Topology
{
    /// <summary>How near a corner the pointer must sit before a wrap will fire.</summary>
    private const int DefaultCornerZone = 16;

    /// <summary>Overshoot at or below this is jitter, not a deliberate shove.</summary>
    private const int EscapeDeadband = 3;

    /// <summary>Sideways slop tolerated while pushing into a corner.</summary>
    private const int DriftTolerance = 2;

    private static readonly Direction[] HorizontalSides = [Direction.Left, Direction.Right];
    private static readonly Direction[] VerticalSides = [Direction.Up, Direction.Down];

    private readonly Rectangle[] _screens;
    private readonly int _cornerZone;

    public Topology(Rectangle[] screens, int cornerZone = DefaultCornerZone)
    {
        _screens = screens;
        _cornerZone = cornerZone;
    }

    public static Topology FromSystem() =>
        new(System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray());

    public IReadOnlyList<Rectangle> Screens => _screens;

    /// <summary>The corner zone in effect, so the debug overlay can draw what the logic actually uses.</summary>
    public int CornerZone => _cornerZone;

    public bool Contains(POINT p)
    {
        foreach (var s in _screens)
            if (s.Contains(p.X, p.Y)) return true;
        return false;
    }

    /// <summary>
    /// Decides whether <paramref name="desired"/> — the position the mouse is
    /// trying to reach — needs to be redirected onto a neighbouring monitor.
    /// </summary>
    /// <param name="desired">Pre-clamp position reported by the hook.</param>
    /// <param name="current">Where the cursor actually is right now.</param>
    /// <returns>True if <paramref name="jump"/> should be applied.</returns>
    public bool TryResolveJump(POINT desired, POINT current, out POINT jump)
    {
        jump = default;

        // Still landing on a real monitor: Windows will handle it correctly.
        if (_screens.Length < 2 || Contains(desired)) return false;

        var src = ScreenContaining(current) ?? NearestScreen(current);
        if (src is not { } source) return false;

        // How far past each edge of the source monitor the target point sits.
        // Only positive values are escapes; try the largest overshoot first so
        // a diagonal flick resolves along the axis it mostly travelled.
        Span<(Direction Dir, int Overshoot)> escapes =
        [
            (Direction.Left,  source.Left - desired.X),
            (Direction.Right, desired.X - (source.Right - 1)),
            (Direction.Up,    source.Top - desired.Y),
            (Direction.Down,  desired.Y - (source.Bottom - 1)),
        ];
        escapes.Sort((a, b) => b.Overshoot.CompareTo(a.Overshoot));

        foreach (var (dir, overshoot) in escapes)
        {
            if (overshoot <= 0) continue;

            // A monitor squarely on that side is the clean case.
            if (FindNeighbour(source, desired, dir) is { } target)
            {
                jump = LandingPoint(target, desired, dir);
                return true;
            }

            // Nothing directly there — but at a corner the monitor you are
            // heading for sits diagonally, which is precisely why the pointer
            // wedges. Carry it around the step.
            if (overshoot > EscapeDeadband && TryCornerWrap(source, desired, current, dir, out jump))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Handles the reflex corners of the desktop outline: the pointer is pinned
    /// against an edge with no monitor beyond it, but a monitor alongside
    /// extends past that edge and is what the user is pushing toward.
    /// </summary>
    private bool TryCornerWrap(Rectangle src, POINT desired, POINT current, Direction escape, out POINT jump)
    {
        jump = default;

        // Only wrap once the pointer is actually held against the edge —
        // otherwise a fast flick could teleport out of open screen.
        if (!IsPinned(src, current, escape)) return false;

        foreach (var side in Perpendicular(escape))
        {
            if (CornerDistance(src, current, side) > _cornerZone) continue;

            // Pushing away from that side means they want the edge, not the wrap.
            if (DriftAway(side, desired, current) > DriftTolerance) continue;

            if (FindNeighbour(src, current, side) is not { } target) continue;

            // Wrapping is pointless unless the neighbour reaches past the edge
            // the pointer is stuck on.
            if (!ExtendsPast(target, src, escape)) continue;

            jump = WrapLanding(target, desired, escape, side);
            return true;
        }

        return false;
    }

    private static Direction[] Perpendicular(Direction d) =>
        IsVertical(d) ? HorizontalSides : VerticalSides;

    private static bool IsVertical(Direction d) => d is Direction.Up or Direction.Down;

    /// <summary>True when the pointer is already held against <paramref name="src"/>'s edge.</summary>
    private static bool IsPinned(Rectangle src, POINT c, Direction d) => d switch
    {
        Direction.Left  => c.X <= src.Left,
        Direction.Right => c.X >= src.Right - 1,
        Direction.Up    => c.Y <= src.Top,
        _               => c.Y >= src.Bottom - 1,
    };

    /// <summary>How far the pointer sits from <paramref name="src"/>'s edge on the given side.</summary>
    private static int CornerDistance(Rectangle src, POINT c, Direction side) => side switch
    {
        Direction.Left  => c.X - src.Left,
        Direction.Right => src.Right - 1 - c.X,
        Direction.Up    => c.Y - src.Top,
        _               => src.Bottom - 1 - c.Y,
    };

    /// <summary>Movement away from the side we would wrap toward; negative means toward it.</summary>
    private static int DriftAway(Direction side, POINT desired, POINT current) => side switch
    {
        Direction.Left  => desired.X - current.X,
        Direction.Right => current.X - desired.X,
        Direction.Up    => desired.Y - current.Y,
        _               => current.Y - desired.Y,
    };

    private static bool ExtendsPast(Rectangle target, Rectangle src, Direction d) => d switch
    {
        Direction.Left  => target.Left < src.Left,
        Direction.Right => target.Right > src.Right,
        Direction.Up    => target.Top < src.Top,
        _               => target.Bottom > src.Bottom,
    };

    /// <summary>
    /// Lands on the neighbour's facing edge, carrying the pointer's position
    /// along the escape axis so the movement reads as continuous.
    /// </summary>
    private static POINT WrapLanding(Rectangle t, POINT desired, Direction escape, Direction side)
    {
        if (IsVertical(escape))
        {
            var x = side == Direction.Left ? t.Right - 1 : t.Left;
            return new POINT(x, Clamp(desired.Y, t.Top, t.Bottom - 1));
        }

        var y = side == Direction.Up ? t.Bottom - 1 : t.Top;
        return new POINT(Clamp(desired.X, t.Left, t.Right - 1), y);
    }

    /// <summary>Picks the closest monitor lying wholly on the given side of <paramref name="source"/>.</summary>
    private Rectangle? FindNeighbour(Rectangle source, POINT desired, Direction dir)
    {
        Rectangle? best = null;
        var bestGap = int.MaxValue;
        var bestOffAxis = int.MaxValue;

        foreach (var s in _screens)
        {
            if (s == source) continue;

            int gap, offAxis;
            switch (dir)
            {
                case Direction.Left:
                    if (s.Right > source.Left) continue;
                    gap = source.Left - s.Right;
                    offAxis = AxisDistance(desired.Y, s.Top, s.Bottom);
                    break;
                case Direction.Right:
                    if (s.Left < source.Right) continue;
                    gap = s.Left - source.Right;
                    offAxis = AxisDistance(desired.Y, s.Top, s.Bottom);
                    break;
                case Direction.Up:
                    if (s.Bottom > source.Top) continue;
                    gap = source.Top - s.Bottom;
                    offAxis = AxisDistance(desired.X, s.Left, s.Right);
                    break;
                default:
                    if (s.Top < source.Bottom) continue;
                    gap = s.Top - source.Bottom;
                    offAxis = AxisDistance(desired.X, s.Left, s.Right);
                    break;
            }

            // Nearest wins; among equally near monitors prefer the one the
            // pointer was already heading at along the other axis.
            if (gap < bestGap || (gap == bestGap && offAxis < bestOffAxis))
            {
                best = s;
                bestGap = gap;
                bestOffAxis = offAxis;
            }
        }

        return best;
    }

    /// <summary>Lands the cursor just inside the target's facing edge, holding the off-axis position.</summary>
    private static POINT LandingPoint(Rectangle target, POINT desired, Direction dir) => dir switch
    {
        Direction.Left  => new POINT(target.Right - 1, Clamp(desired.Y, target.Top, target.Bottom - 1)),
        Direction.Right => new POINT(target.Left,      Clamp(desired.Y, target.Top, target.Bottom - 1)),
        Direction.Up    => new POINT(Clamp(desired.X, target.Left, target.Right - 1), target.Bottom - 1),
        _               => new POINT(Clamp(desired.X, target.Left, target.Right - 1), target.Top),
    };

    private Rectangle? ScreenContaining(POINT p)
    {
        foreach (var s in _screens)
            if (s.Contains(p.X, p.Y)) return s;
        return null;
    }

    private Rectangle? NearestScreen(POINT p)
    {
        Rectangle? best = null;
        var bestDist = long.MaxValue;

        foreach (var s in _screens)
        {
            long dx = AxisDistance(p.X, s.Left, s.Right);
            long dy = AxisDistance(p.Y, s.Top, s.Bottom);
            var dist = dx * dx + dy * dy;
            if (dist < bestDist) { best = s; bestDist = dist; }
        }

        return best;
    }

    /// <summary>Distance from <paramref name="v"/> to the half-open interval [lo, hi); 0 when inside.</summary>
    private static int AxisDistance(int v, int lo, int hi)
    {
        if (v < lo) return lo - v;
        if (v >= hi) return v - hi + 1;
        return 0;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

    public string Describe() =>
        string.Join(Environment.NewLine, _screens.Select((s, i) =>
            $"  [{i}] x={s.X} y={s.Y} w={s.Width} h={s.Height}"));
}
