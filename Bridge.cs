using System.Runtime.InteropServices;

namespace MouseBridge;

/// <summary>
/// Installs the low-level mouse hook and redirects the cursor whenever it is
/// about to snag on a step between vertically offset monitors.
/// </summary>
internal sealed class Bridge : IDisposable
{
    // Held in a field so the GC cannot collect the delegate while the OS still
    // holds the function pointer — collecting it crashes the input thread.
    private readonly LowLevelMouseProc _callback;
    private readonly Logger _log;

    private IntPtr _hook;
    private Topology _topology;

    public bool Enabled { get; set; } = true;

    public long JumpCount { get; private set; }

    /// <summary>
    /// A second cursor that only ever moves by the amount the mouse moved. It
    /// starts on the real cursor and is not stopped by the edge of the desktop,
    /// so at an edge it shows where the mouse is really trying to go.
    /// </summary>
    public POINT VirtualCursor { get; private set; }

    public Bridge(Logger log)
    {
        _log = log;
        _callback = OnMouseEvent;
        _topology = Topology.FromSystem();
    }

    public Topology Topology => _topology;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;

        // WH_MOUSE_LL is a global hook but does not need a real module handle;
        // passing the EXE's is the documented convention.
        var module = Native.GetModuleHandle(null);
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _callback, module, 0);

        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException(
                $"SetWindowsHookEx failed (error {Marshal.GetLastWin32Error()}).");

        // So the virtual cursor has somewhere sensible to sit before the mouse
        // has moved even once.
        if (Native.GetCursorPos(out var start)) VirtualCursor = start;

        _log.Write("hook installed");
        _log.Write("monitors:" + Environment.NewLine + _topology.Describe());
    }

    /// <summary>Re-reads the monitor layout; call when displays change.</summary>
    public void RefreshTopology()
    {
        _topology = Topology.FromSystem();
        _log.Write("topology refreshed:" + Environment.NewLine + _topology.Describe());
    }

    private IntPtr OnMouseEvent(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != Native.HC_ACTION || (int)wParam != Native.WM_MOUSEMOVE)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        // Ignore synthetic movement — including the SetCursorPos calls we make
        // below, which would otherwise feed straight back into this callback.
        if ((data.flags & Native.LLMHF_INJECTED) != 0)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        if (!Native.GetCursorPos(out var current))
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        var topology = _topology;
        TrackVirtualCursor(data.pt, current, topology);

        // Tracking carries on while paused, so the virtual cursor is still in
        // the right place when the bridge is switched back on.
        if (!Enabled)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        _log.Trace(data.pt, current, topology);

        if (topology.TryResolveJump(data.pt, current, out var jump))
        {
            Native.SetCursorPos(jump.X, jump.Y);
            JumpCount++;
            _log.Write($"jump {current} -> want {data.pt} -> landed {jump}");

            // Swallow the event: letting it through would apply Windows' own
            // clamp and drag the cursor straight back to the edge we just left.
            return 1;
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Keeps the virtual cursor up to date.
    /// </summary>
    /// <param name="reported">Where this move wants to land, before any clamping.</param>
    /// <param name="actual">Where the cursor is right now, this move not yet applied.</param>
    /// <remarks>
    /// While the mouse still has room the two are the same thing. The virtual
    /// one only goes its own way once the edge of the desktop starts holding
    /// the real one back, and it rejoins as soon as the mouse is free again.
    /// Letting it run on the whole time is no good in practice: every shove at
    /// an edge and every jump leaves it further out, and it never comes back.
    /// Internal rather than private so it can be exercised without a real mouse.
    /// </remarks>
    internal void TrackVirtualCursor(POINT reported, POINT actual, Topology topology)
    {
        if (topology.Contains(reported))
        {
            VirtualCursor = reported;
            return;
        }

        // Held at an edge: add the step this move wanted to take. The gap
        // between two reported positions would not do, because at an edge each
        // one is measured from the pinned cursor and so stops growing.
        VirtualCursor = new POINT(
            VirtualCursor.X + (reported.X - actual.X),
            VirtualCursor.Y + (reported.Y - actual.Y));
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _log.Write("hook removed");
    }
}
