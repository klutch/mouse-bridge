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
        if (nCode != Native.HC_ACTION || (int)wParam != Native.WM_MOUSEMOVE || !Enabled)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        // Ignore synthetic movement — including the SetCursorPos calls we make
        // below, which would otherwise feed straight back into this callback.
        if ((data.flags & Native.LLMHF_INJECTED) != 0)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        if (!Native.GetCursorPos(out var current))
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        var topology = _topology;
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

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _log.Write("hook removed");
    }
}
