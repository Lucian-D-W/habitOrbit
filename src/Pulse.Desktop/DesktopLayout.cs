using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pulse.Desktop;

public sealed record DisplayArea(string Id, NativeWindow.Rect Work, uint Dpi, bool Primary)
{
    public ScreenPoint Restore(OrbPlacement p) => Clamp(new(checked((int)Math.Round(Work.Left+p.CenterXDip*Dpi/96)),
        checked((int)Math.Round(Work.Top+p.CenterYDip*Dpi/96))));
    public ScreenPoint Clamp(ScreenPoint p)
    {
        var margin=(int)Math.Round(97*Dpi/96d);
        var mx=Math.Min(margin,(Work.Right-Work.Left)/2);var my=Math.Min(margin,(Work.Bottom-Work.Top)/2);
        return new(Math.Clamp(p.X,Work.Left+mx,Work.Right-mx),Math.Clamp(p.Y,Work.Top+my,Work.Bottom-my));
    }
    public OrbPlacement Save(ScreenPoint p,bool visible) => new(visible,Id,(p.X-Work.Left)*96d/Dpi,(p.Y-Work.Top)*96d/Dpi);
}
public static class DesktopLayout
{
    public static IReadOnlyList<DisplayArea> Displays()
    {
        var list=new List<DisplayArea>();Exception? failure=null;
        MonitorCallback callback=(nint monitor,nint dc,ref NativeWindow.Rect rect,nint data)=>
        {
            try
            {
                var info=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
                if(!GetMonitorInfoW(monitor,ref info))throw new Win32Exception(Marshal.GetLastWin32Error());
                var device=new DisplayDevice{Size=(uint)Marshal.SizeOf<DisplayDevice>()};
                var id=EnumDisplayDevicesW(info.Device,0,ref device,1) && !string.IsNullOrEmpty(device.Id)?device.Id:info.Device;
                var hr=GetDpiForMonitor(monitor,0,out var x,out _);
                if(hr<0)throw new Win32Exception(hr);
                list.Add(new(id,info.Work,x,(info.Flags&1)!=0));return true;
            }
            catch(Exception e){failure=e;return false;}
        };
        if(!EnumDisplayMonitors(0,0,callback,0))throw failure??new Win32Exception(Marshal.GetLastWin32Error());
        return list.OrderByDescending(x=>x.Primary).ToArray();
    }
    public static DisplayArea Nearest(IReadOnlyList<DisplayArea> displays,ScreenPoint p) => displays.MinBy(d=>
        Math.Pow(p.X-Math.Clamp(p.X,d.Work.Left,d.Work.Right),2)+Math.Pow(p.Y-Math.Clamp(p.Y,d.Work.Top,d.Work.Bottom),2))
        ??throw new InvalidOperationException("没有可用显示器。");
    public static ScreenPoint FreePosition(IReadOnlyList<DisplayArea> displays,IReadOnlyList<ScreenPoint> occupied)
    {
        foreach(var d in displays)
        {
            var gap=(int)Math.Round(150*d.Dpi/96d);var margin=(int)Math.Round(110*d.Dpi/96d);
            for(var y=d.Work.Top+margin;y<d.Work.Bottom-margin;y+=gap)
                for(var x=d.Work.Left+margin;x<d.Work.Right-margin;x+=gap)
                    if(occupied.All(p=>Math.Sqrt(Math.Pow(x-p.X,2)+Math.Pow(y-p.Y,2))>=gap))return new(x,y);
        }
        var first=displays[0];return first.Clamp(new(first.Work.Left+110,first.Work.Top+110));
    }
    private delegate bool MonitorCallback(nint monitor,nint dc,ref NativeWindow.Rect rect,nint data);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct MonitorInfo
    {public uint Size;public NativeWindow.Rect Monitor,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct DisplayDevice
    {public uint Size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;
     public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;}
    [DllImport("user32.dll",SetLastError=true)]private static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorCallback callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetMonitorInfoW(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool EnumDisplayDevicesW(string device,uint index,ref DisplayDevice output,uint flags);
    [DllImport("shcore.dll")]private static extern int GetDpiForMonitor(nint monitor,uint type,out uint x,out uint y);
}
