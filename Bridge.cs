using System.Runtime.InteropServices;

namespace MouseBridge;

/// <summary>
/// Watches the mouse and keeps the cursor on the desktop.
/// </summary>
internal sealed class Bridge : IDisposable
{
    // Held in a field so the GC cannot collect the delegate while the OS still
    // holds the function pointer — collecting it crashes the input thread.
    private readonly LowLevelMouseProc _callback;

    private IntPtr _hook;
    private Topology _topology;

    public Topology Topology => _topology;

    public Bridge()
    {
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
    }

    /// <summary>Re-reads the monitor layout; call when displays change.</summary>
    public void RefreshTopology() => _topology = Topology.FromSystem();

    private IntPtr OnMouseEvent(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != Native.HC_ACTION || (int)wParam != Native.WM_MOUSEMOVE)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        // Movement put there by software is not the mouse moving, so it is no
        // use for working out how far the mouse travelled.
        if ((data.flags & Native.LLMHF_INJECTED) != 0)
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

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
