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

    public static POINT operator +(POINT a, POINT b)
    {
        return new POINT(a.X + b.X, a.Y + b.Y);
    }

    public static POINT operator -(POINT a, POINT b)
    {
        return new POINT(a.X - b.X, a.Y - b.Y);
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
    public static extern bool DestroyIcon(IntPtr hIcon);
}
