using System.Runtime.InteropServices;

namespace MouseBridge;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y) { X = x; Y = y; }

    public override string ToString() => $"({X},{Y})";

    public Point ToPoint()
    {
        return new Point(X, Y);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

internal static class Native
{
    public const int WH_MOUSE_LL = 14;
    public const int HC_ACTION = 0;
    public const int WM_MOUSEMOVE = 0x0200;

    /// <summary>Set in MSLLHOOKSTRUCT.flags when the event came from SendInput / SetCursorPos.</summary>
    public const uint LLMHF_INJECTED = 0x00000001;

    // Window styles that keep the overlay boxes inert: never focusable, absent
    // from Alt+Tab, and transparent to the mouse they exist to illustrate.
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int WM_DPICHANGED = 0x02E0;

    /// <summary>Asks a window which part of itself is under a point.</summary>
    public const int WM_NCHITTEST = 0x0084;

    /// <summary>The answer meaning "none of me — ask whatever is behind instead".</summary>
    public const int HTTRANSPARENT = -1;

    /// <summary>Sent before a click reaches a window, to ask what to do about focus.</summary>
    public const int WM_MOUSEACTIVATE = 0x0021;

    /// <summary>The answer meaning "do not take focus, and swallow nothing".</summary>
    public const int MA_NOACTIVATE = 3;

    /// <summary>Sent when a window is about to move; ignoring it lets the move be silent.</summary>
    public const int WM_WINDOWPOSCHANGING = 0x0046;

    // Flags for SetWindowPos, chosen so moving the dot disturbs as little as
    // possible: no focus change, no z-order shuffle, no size change, and no
    // messages sent out about any of it.
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_NOSENDCHANGING = 0x0400;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
