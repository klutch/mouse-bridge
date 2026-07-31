using System.Drawing;

namespace MouseBridge;

/// <summary>
/// Scenario checks for the jump rule, run against the layout that motivated
/// the tool: a 1080x1920 portrait panel to the left of a 1920x1080 primary,
/// offset 301px upward so neither the top nor the bottom edges line up.
/// </summary>
internal static class Tests
{
    private static readonly Rectangle Portrait = new(-1080, -301, 1080, 1920); // spans y -301..1618
    private static readonly Rectangle Primary = new(0, 0, 1920, 1080);         // spans y    0..1079

    private static int _failures;

    private static int Main()
    {
        var real = new Topology([Portrait, Primary]);

        // --- The snag being fixed: leaving the portrait panel rightward from
        // the bands of it that sit above or below the primary. ---
        Jumps(real, "escape right, below primary's bottom",
            current: new POINT(-1, 1500), desired: new POINT(2, 1500), expected: new POINT(0, 1079));

        Jumps(real, "escape right, above primary's top",
            current: new POINT(-1, -200), desired: new POINT(2, -200), expected: new POINT(0, 0));

        Jumps(real, "diagonal flick up-and-right from the low band",
            current: new POINT(-1, 1500), desired: new POINT(40, 1400), expected: new POINT(0, 1079));

        Jumps(real, "corner escape: down has no neighbour, falls through to right",
            current: new POINT(-1, 1618), desired: new POINT(3, 1624), expected: new POINT(0, 1079));

        // --- Corner wrap. The desktop outline has two reflex corners, (0,0)
        // and (0,1080), and the pointer wedges in both. Nothing lies directly
        // beyond the edge being pushed against; the target sits diagonally. ---
        Jumps(real, "bottom corner: pinned at (0,1079), shoved down",
            current: new POINT(0, 1079), desired: new POINT(0, 1086), expected: new POINT(-1, 1086));

        Jumps(real, "top corner: pinned at (0,0), shoved up",
            current: new POINT(0, 0), desired: new POINT(0, -6), expected: new POINT(-1, -6));

        Jumps(real, "top corner, a few px in from the edge",
            current: new POINT(5, 0), desired: new POINT(5, -6), expected: new POINT(-1, -6));

        Jumps(real, "bottom corner, at the edge of the corner zone",
            current: new POINT(12, 1079), desired: new POINT(12, 1086), expected: new POINT(-1, 1086));

        // --- Guards on the wrap: it must not fire away from the corner, on
        // jitter, before the pointer is pinned, or toward a monitor that does
        // not actually reach past the edge. ---
        Passes(real, "wrap ignores a shove well clear of the corner",
            current: new POINT(500, 1079), desired: new POINT(500, 1086));

        Passes(real, "wrap waits until the pointer is pinned",
            current: new POINT(0, 1000), desired: new POINT(0, 1086));

        Passes(real, "wrap ignores jitter inside the deadband",
            current: new POINT(0, 1079), desired: new POINT(0, 1081));

        Passes(real, "wrap ignores a shove heading away from the corner",
            current: new POINT(0, 1079), desired: new POINT(9, 1086));

        Passes(real, "no wrap when the neighbour stops short of the edge",
            current: new POINT(-1, 1618), desired: new POINT(-1, 1624));

        // --- Where the monitors already overlap, Windows is correct and we
        // must keep our hands off. ---
        Passes(real, "leftward crossing inside the overlap",
            current: new POINT(5, 500), desired: new POINT(-3, 500));

        Passes(real, "rightward crossing inside the overlap",
            current: new POINT(-1, 500), desired: new POINT(3, 500));

        // --- Genuine desktop edges: no teleporting allowed. ---
        Passes(real, "off the top of the primary",
            current: new POINT(500, 0), desired: new POINT(500, -6));

        Passes(real, "off the right of the primary",
            current: new POINT(1919, 500), desired: new POINT(1925, 500));

        Passes(real, "off the bottom of the portrait panel",
            current: new POINT(-500, 1618), desired: new POINT(-500, 1625));

        Passes(real, "off the left of the portrait panel",
            current: new POINT(-1080, 500), desired: new POINT(-1086, 500));

        // --- Other layouts. ---
        Passes(new Topology([Primary]), "single monitor never jumps",
            current: new POINT(1919, 500), desired: new POINT(1930, 500));

        var stacked = new Topology([new Rectangle(0, 0, 1920, 1080), new Rectangle(600, -1080, 1920, 1080)]);
        Jumps(stacked, "vertically stacked, horizontally offset: escape upward",
            current: new POINT(300, 0), desired: new POINT(300, -4), expected: new POINT(600, -1));

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "all scenarios passed" : $"{_failures} scenario(s) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void Jumps(Topology t, string name, POINT current, POINT desired, POINT expected)
    {
        if (!t.TryResolveJump(desired, current, out var jump))
        {
            Fail(name, $"expected a jump to {expected}, but the move was passed through");
            return;
        }

        if (jump.X != expected.X || jump.Y != expected.Y)
        {
            Fail(name, $"expected {expected}, got {jump}");
            return;
        }

        Pass(name, $"{current} -> {desired} lands {jump}");
    }

    private static void Passes(Topology t, string name, POINT current, POINT desired)
    {
        if (t.TryResolveJump(desired, current, out var jump))
        {
            Fail(name, $"expected pass-through, but it jumped to {jump}");
            return;
        }

        Pass(name, $"{current} -> {desired} passed through");
    }

    private static void Pass(string name, string detail) =>
        Console.WriteLine($"  ok    {name,-58} {detail}");

    private static void Fail(string name, string detail)
    {
        _failures++;
        Console.WriteLine($"  FAIL  {name,-58} {detail}");
    }
}
