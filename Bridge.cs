using System.Runtime.InteropServices;

namespace MouseBridge;

/// <summary>
/// Watches the mouse and keeps the virtual cursor up to date.
/// </summary>
internal sealed class Bridge : IDisposable
{
    // Held in a field so the GC cannot collect the delegate while the OS still
    // holds the function pointer — collecting it crashes the input thread.
    private readonly LowLevelMouseProc _callback;
    private readonly Logger _log;

    private IntPtr _hook;
    private Topology _topology;

    /// <summary>
    /// A second cursor that is not stopped by the edge of the desktop. It sits
    /// on the real cursor while the mouse has room, and carries on past the edge
    /// while the real one is held back, showing where the mouse is trying to go.
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

        // Movement put there by software is not the mouse moving, so it is no
        // use for working out how far the mouse travelled.
        if ((data.flags & Native.LLMHF_INJECTED) != 0)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        if (!Native.GetCursorPos(out var current))
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        var topology = _topology;
        TrackVirtualCursor(data.pt, current, topology);
        _log.Trace(data.pt, current, topology);

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
    /// an edge leaves it further out, and it never comes back.
    /// Internal rather than private so it can be exercised without a real mouse.
    /// </remarks>
    internal void TrackVirtualCursor(POINT reported, POINT actual, Topology topology)
    {
        VirtualCursor = new POINT(Cursor.Position.X, Cursor.Position.Y);
        VirtualCursor += reported - actual;

        if (Topology.Contains(VirtualCursor))
        {
            Cursor.Position = VirtualCursor.ToPoint();
        }
        else
        {
            VirtualCursor = Topology.Clamp(VirtualCursor);
        }
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _log.Write("hook removed");
    }
}
