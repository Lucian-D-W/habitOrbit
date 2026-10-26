using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pulse.Host;

internal sealed class NativeTray:IDisposable
{
    private readonly nint hwnd,icon;
    private readonly Action open,toggle,exit;
    private readonly uint taskbarCreated=RegisterWindowMessageW("TaskbarCreated");
    private IconData data;
    private bool temporaryHidden;
    public NativeTray(nint hwnd,Action open,Action toggle,Action exit)
    {
        this.hwnd=hwnd;this.open=open;this.toggle=toggle;this.exit=exit;
        icon=LoadImageW(0,Path.Combine(AppContext.BaseDirectory,"pulse.ico"),1,0,0,0x10|0x40);
        if(icon==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"托盘图标加载失败。");
        data=new(){Size=(uint)Marshal.SizeOf<IconData>(),Hwnd=hwnd,Id=1,Flags=1|2|4|0x80,Callback=0x8002,Icon=icon,Tip="星以为常 habitOrbit · 手动累积记录",Info="",Title=""};
        Add();
    }
    private void Add()
    {
        if(!Shell_NotifyIconW(0,ref data))throw new Win32Exception(Marshal.GetLastWin32Error(),"托盘入口创建失败。");
        data.Version=4;
        if(!Shell_NotifyIconW(4,ref data))throw new Win32Exception(Marshal.GetLastWin32Error(),"托盘版本设置失败。");
    }
    public bool Message(uint message,nuint wp,nint lp)
    {
        if(message==taskbarCreated){Add();return true;}
        if(message!=0x8002)return false;
        var notification=(uint)((long)lp&0xffff);
        if(notification is 0x400 or 0x401)open();
        if(notification is 0x7b or 0x205)
        {
            var menu=CreatePopupMenu();if(menu==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                AppendMenuW(menu,0,1,"打开管理");AppendMenuW(menu,0,2,temporaryHidden?"恢复桌面星球":"临时隐藏全部");
                AppendMenuW(menu,0x800,0,null);AppendMenuW(menu,0,3,"完全退出 星以为常 habitOrbit");
                GetCursorPos(out var point);SetForegroundWindow(hwnd);
                var choice=TrackPopupMenu(menu,0x100|0x80|2,point.X,point.Y,0,hwnd,0);
                PostMessageW(hwnd,0,0,0);
                if(choice==1)open();if(choice==2){toggle();temporaryHidden=!temporaryHidden;}if(choice==3)exit();
            }
            finally{DestroyMenu(menu);}
        }
        return true;
    }
    public void Balloon(string title,string text)
    {
        data.Flags|=0x10;data.Title=title;data.Info=text.Length>255?text[..255]:text;data.InfoFlags=2;
        Shell_NotifyIconW(1,ref data);data.Flags&=~0x10u;data.Info="";data.Title="";
    }
    public void Dispose(){Shell_NotifyIconW(2,ref data);DestroyIcon(icon);}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct IconData
    {
        public uint Size;public nint Hwnd;public uint Id,Flags,Callback;public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Title;
        public uint InfoFlags;public Guid Guid;public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool Shell_NotifyIconW(uint command,ref IconData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint LoadImageW(nint instance,string path,uint type,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll",SetLastError=true)]private static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool AppendMenuW(nint menu,uint flags,nuint id,string? text);
    [DllImport("user32.dll")]private static extern uint TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint owner,nint rect);
    [DllImport("user32.dll")]private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern bool PostMessageW(nint hwnd,uint message,nuint wp,nint lp);
}
