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

    public Topology Topology => _topology;

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

        if (_topology.Contains(data.pt))
        {
            Cursor.Position = data.pt.ToPoint();
        }
        else
        {
            Cursor.Position = _topology.Clamp(data.pt).ToPoint();
            return 1;
        }

        _log.Trace(data.pt, current, _topology);

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _log.Write("hook removed");
    }
}
