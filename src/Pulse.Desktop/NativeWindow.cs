using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pulse.Desktop;

public static class NativeWindow
{
    public const uint Layered = 0x80000, Transparent = 0x20, NoActivate = 0x08000000, ToolWindow = 0x80, Topmost = 8;
    public const uint Popup = 0x80000000, OverlappedWindow = 0x00cf0000;
    private const string ClassName = "Pulse.Native.Orb.v1";
    private static readonly Dictionary<nint, Func<uint, nuint, nint, nint?>> callbacks = new();
    private static readonly WindowProcedure procedure = Dispatch;
    private static bool registered;
    public static Exception? CallbackFailure { get; private set; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam);
    private static nint Dispatch(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (callbacks.TryGetValue(hwnd, out var handler) && handler(message, wParam, lParam) is nint result) return result;
        }
        catch (Exception error) { CallbackFailure = error; PostQuitMessage(1); }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    public static nint Create(string title, uint style, uint exStyle, int x, int y, int width, int height,
        Func<uint, nuint, nint, nint?> handler, nint owner = 0)
    {
        if (!registered)
        {
            var cls = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
                Instance = GetModuleHandleW(null), Cursor = LoadCursorW(0, 32512), ClassName = ClassName, Background = 6 };
            if (RegisterClassExW(ref cls) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            registered = true;
        }
        var hwnd = CreateWindowExW(exStyle, ClassName, title, style, x, y, width, height, owner, 0, GetModuleHandleW(null), 0);
        if (hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        callbacks.Add(hwnd, handler);
        return hwnd;
    }
    public static void Destroy(nint hwnd) { if (hwnd == 0) return; DestroyWindow(hwnd); callbacks.Remove(hwnd); }
    public static ScreenPoint CursorPosition()
    {
        if (!GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(point.X, point.Y);
    }
    public static void SetCircle(nint hwnd, int pixels)
    {
        var region = CreateEllipticRgn(0, 0, pixels, pixels);
        if (region == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (SetWindowRgn(hwnd, region, false) == 0) { DeleteObject(region); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        // The successful SetWindowRgn owns the HRGN.
    }
    public static bool CircleContains(nint hwnd, int x, int y)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try { if (GetWindowRgn(hwnd, region) == 0) throw new Win32Exception(Marshal.GetLastWin32Error()); return PtInRegion(region, x, y); }
        finally { DeleteObject(region); }
    }
    public static ScreenPoint ClampToWorkArea(ScreenPoint center, int margin)
    {
        var point = new Point { X = center.X, Y = center.Y };
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromPoint(point, 2), ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var r = info.Work;
        return new(Math.Clamp(center.X, r.Left + Math.Min(margin, (r.Right-r.Left)/2), r.Right - Math.Min(margin, (r.Right-r.Left)/2)),
            Math.Clamp(center.Y, r.Top + Math.Min(margin, (r.Bottom-r.Top)/2), r.Bottom - Math.Min(margin, (r.Bottom-r.Top)/2)));
    }
    public static int MessageLoop()
    {
        int status;
        while ((status = GetMessageW(out var message, 0, 0, 0)) > 0) { TranslateMessage(ref message); DispatchMessageW(ref message); }
        if (status < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (CallbackFailure is not null) throw new InvalidOperationException("Native window callback failed.", CallbackFailure);
        return 0;
    }
    public static void Button(nint parent, int id, string text, int x, int y, int width = 115) =>
        CreateWindowExW(0, "BUTTON", text, 0x50010000, x, y, width, 30, parent, id, GetModuleHandleW(null), 0);

    [StructLayout(LayoutKind.Sequential)] private struct WindowClass
    {
        public uint Size, Style; public nint Procedure; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Pt; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] public struct PowerStatus { public byte ACLine, BatteryFlags, BatteryPercent, Saver; public uint LifeTime, FullLifeTime; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseTracking { public uint Size, Flags; public nint Hwnd; public uint HoverTime; }
    [DllImport("user32.dll", SetLastError = true)] public static extern bool TrackMouseEvent(ref MouseTracking tracking);
    [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClass cls);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")] private static extern nint LoadCursorW(nint instance, nint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint extended, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint hwnd, uint message, nuint wp, nint lp);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern nint GetCapture();
    [DllImport("user32.dll", SetLastError = true)] public static extern nuint SetTimer(nint hwnd, nuint id, uint interval, nint callback);
    [DllImport("user32.dll")] public static extern bool KillTimer(nint hwnd, nuint id);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int result);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessageW(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowTextW(nint hwnd, string title);
    [DllImport("gdi32.dll")] private static extern nint CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(nint process, uint flags);
}
