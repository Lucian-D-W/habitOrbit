using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Pulse.Desktop;

public static class ProcessEnvironment
{
    public static bool IsPackaged()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        if (result == 15700) return false; // APPMODEL_ERROR_NO_PACKAGE
        if (result == 122 || result == 0) return true;
        throw new Win32Exception(result);
    }

    public static string LocalAppData()
    {
        var folder = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref folder, 0, IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStringUni(pointer) ?? throw new IOException("LocalAppData missing."); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    public static string DataDirectory()
    {
        // This product is deployed unpackaged. Do not silently create a second virtualized DB.
        if (IsPackaged()) throw new InvalidOperationException("Pulse requires unpackaged deployment; packaged data access is not configured.");
        return Path.Combine(LocalAppData(), "Pulse");
    }

    public static string PhysicalDirectory(string path)
    {
        using var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(32768);
        var size = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (size == 0 || size >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        return buffer.ToString();
    }

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr packageFullName);
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint length, uint flags);
}
