using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pulse.Desktop;

public sealed class LayeredSurface : IDisposable
{
    private nint dc, bitmap, oldBitmap, pixels;
    public int Diameter { get; }
    public LayeredSurface(int diameter)
    {
        Diameter = diameter;
        dc = CreateCompatibleDC(0);
        if (dc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new BitmapInfo { Size = 40, Width = diameter, Height = -diameter, Planes = 1, BitCount = 32 };
        bitmap = CreateDIBSection(dc, ref info, 0, out pixels, 0, 0);
        if (bitmap == 0) { DeleteDC(dc); dc = 0; throw new Win32Exception(Marshal.GetLastWin32Error()); }
        oldBitmap = SelectObject(dc, bitmap);
        if (oldBitmap == 0 || oldBitmap == -1) { Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }
    public void Present(nint hwnd, byte[] bgra, ScreenPoint center)
    {
        if (bgra.Length != Diameter * Diameter * 4) throw new ArgumentException("Frame dimensions differ from the DIB.", nameof(bgra));
        Marshal.Copy(bgra, 0, pixels, bgra.Length);
        var destination = new NativeWindow.Point { X = center.X - Diameter/2, Y = center.Y - Diameter/2 };
        var origin = new NativeWindow.Point();
        var size = new NativeWindow.Size { Width = Diameter, Height = Diameter };
        var blend = new Blend { Alpha = 255, Format = 1 };
        if (!UpdateLayeredWindow(hwnd, 0, ref destination, ref size, dc, ref origin, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateLayeredWindow failed.");
    }
    public void Dispose()
    {
        if (dc != 0 && oldBitmap != 0 && oldBitmap != -1) SelectObject(dc, oldBitmap);
        if (bitmap != 0) NativeWindow.DeleteObject(bitmap);
        if (dc != 0) DeleteDC(dc);
        dc = bitmap = oldBitmap = pixels = 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XPels, YPels; public uint ColorsUsed, Important, Color;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint source);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint hwnd, nint destinationDc,
        ref NativeWindow.Point destination, ref NativeWindow.Size size, nint sourceDc, ref NativeWindow.Point source, uint key, ref Blend blend, uint flags);
}
