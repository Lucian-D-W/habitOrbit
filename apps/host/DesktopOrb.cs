using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Pulse.Desktop;
using Pulse.Rendering;

namespace Pulse.Host;

internal sealed class DesktopOrb : IDisposable
{
    private OrbRenderer renderer;
    private CanvasRenderTarget coreTarget = null!, decorationTarget = null!;
    private LayeredSurface coreSurface = null!, decorationSurface = null!;
    private readonly OrbGesture gesture = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private ScreenPoint center = new(400, 350);
    private uint dpi = 96;
    private uint pendingDpi;
    private ScreenPoint? pendingCenter;
    private bool releasing, visible, updating, disposed, rightPressed, economy;
    private float energy = .35f, targetEnergy = .35f;
    private double previousTime;
    private double hoverUntil;
    private int count;
    private readonly CanvasDevice? sharedDevice;
    private readonly bool externalAnimation;
    private readonly float sizeDip;
    private OrbVisualState? committedState;
    private string? feedbackError;
    public Action<bool>? CountRequested { get; set; }
    public Action<ScreenPoint>? PositionSaved { get; set; }
    public Action? EnvironmentChanged { get; set; }
    public ScreenPoint Center => center;
    public bool Visible => visible;
    public bool Locked { get; private set; }
    public bool HasFeedback => clock.Elapsed.TotalSeconds < hoverUntil;
    private long frames;
    private double renderMilliseconds;
    public nint Core { get; private set; }
    public nint Decoration { get; private set; }
    public int CorePixels => coreSurface.Diameter;
    public int DecorationPixels => decorationSurface.Diameter;
    public OrbIdentity Identity => renderer.Identity;

    public DesktopOrb(OrbIdentity identity, CanvasDevice? device = null, bool sharedAnimation = false, float coreSizeDip = 144)
    {
        sharedDevice = device; externalAnimation = sharedAnimation; sizeDip = coreSizeDip;
        renderer = new(identity, sharedDevice);
        var styles = NativeWindow.Layered | NativeWindow.NoActivate | NativeWindow.ToolWindow | NativeWindow.Topmost;
        Decoration = NativeWindow.Create("Pulse decoration", NativeWindow.Popup, styles | NativeWindow.Transparent, 0, 0, 248, 248, DecorationMessage);
        Core = NativeWindow.Create("Pulse core", NativeWindow.Popup, styles, 0, 0, 144, 144, CoreMessage, Decoration);
        if (!NativeWindow.WTSRegisterSessionNotification(Core, 0))
        {
            var error = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not register session notifications.");
            Dispose();
            throw error;
        }
        dpi = NativeWindow.GetDpiForWindow(Core);
        Resize(dpi);
    }

    private void Resize(uint newDpi)
    {
        if (updating) return;
        updating = true;
        try
        {
            dpi = newDpi == 0 ? 96 : newDpi;
            ReleaseSurfaces();
            var core = (int)Math.Round(sizeDip * (committedState?.CoreScale??.7f) * dpi / 96);
            var decoration = (int)Math.Round(OrbRenderer.DecorationDiameter * sizeDip / OrbRenderer.CoreDiameter * dpi / 96);
            coreTarget = renderer.CreateTarget(core); decorationTarget = renderer.CreateTarget(decoration);
            coreSurface = new(core); decorationSurface = new(decoration);
            NativeWindow.SetCircle(Core, core);
            center = NativeWindow.ClampToWorkArea(center, decoration / 2);
            RenderFrame();
        }
        finally { updating = false; }
    }

    public void RenderFrame()
    {
        // WM_DPICHANGED can arrive reentrantly inside presentation. Rebuild on the next frame.
        if (pendingDpi != 0 && !updating)
        {
            var requested = pendingDpi; pendingDpi = 0;
            if (pendingCenter is ScreenPoint point) center = point;
            pendingCenter = null; Resize(requested);
        }
        var start = Stopwatch.GetTimestamp();
        var now = clock.Elapsed.TotalSeconds;
        energy = targetEnergy + (energy - targetEnergy) * (float)Math.Exp(-(now - previousTime) / .065);
        previousTime = now;
        var state = committedState is OrbVisualState actual ? actual with { Energy = energy,
            HoverText = now < hoverUntil ? feedbackError ?? actual.HoverText : null } :
            new OrbVisualState(energy, count, now < hoverUntil ? $"{Identity.Name} · {count} 份\n开发反馈，尚未保存" : null);
        decorationSurface.Present(Decoration, renderer.Render(decorationTarget, state, now, OrbLayer.Decoration), center);
        coreSurface.Present(Core, renderer.Render(coreTarget, state, now, OrbLayer.Core), center);
        renderMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        frames++;
    }

    public void Show(bool show)
    {
        if (visible == show) return;
        visible = show;
        if (show) { RenderFrame(); NativeWindow.ShowWindow(Decoration, 4); NativeWindow.ShowWindow(Core, 4); SetAnimationTimer(); }
        else { EndGesture(); NativeWindow.KillTimer(Core, 1); NativeWindow.ShowWindow(Core, 0); NativeWindow.ShowWindow(Decoration, 0); }
    }
    private void SetAnimationTimer()
    {
        if (externalAnimation) return;
        var saver = NativeWindow.GetSystemPowerStatus(out var power) && (power.Saver != 0 || power.ACLine == 0);
        if (NativeWindow.SetTimer(Core, 1, economy || saver ? 67u : 33u, 0) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    public void SetEnergy(float value) => targetEnergy = Math.Clamp(value, 0, 1);
    public void ApplyState(OrbVisualState state,bool clearError=true)
    {
        committedState=state;if(clearError)feedbackError=null;SetEnergy(state.Energy);
        if(CorePixels!=(int)Math.Round(sizeDip*state.CoreScale*dpi/96f))Resize(dpi);
    }
    public void ShowError(string message) { feedbackError = message; hoverUntil = clock.Elapsed.TotalSeconds + 4; }
    public void Place(ScreenPoint point) { center = NativeWindow.ClampToWorkArea(point, DecorationPixels / 2); RenderFrame(); }
    public void ToggleVisible() => Show(!visible);
    public void ToggleEconomy() { economy = !economy; if (visible) SetAnimationTimer(); }
    public void Recenter() { EndGesture(); center = NativeWindow.ClampToWorkArea(new(400,350), DecorationPixels/2); RenderFrame(); }
    public void SetIdentity(OrbIdentity identity)
    {
        EndGesture(); ReleaseSurfaces(); renderer.Dispose(); renderer = new(identity, sharedDevice); Resize(dpi);
    }

    private nint? DecorationMessage(uint message, nuint wp, nint lp) => message == 0x21 ? 3 : null;
    private nint? CoreMessage(uint message, nuint wp, nint lp)
    {
        switch (message)
        {
            case 0x21: return 3; // MA_NOACTIVATE keeps the actual button message.
            case 0x201: // WM_LBUTTONDOWN; class deliberately has no CS_DBLCLKS.
                rightPressed = false;
                gesture.Press(NativeWindow.CursorPosition(), center, NativeWindow.GetSystemMetricsForDpi(68, dpi), NativeWindow.GetSystemMetricsForDpi(69, dpi));
                NativeWindow.SetCapture(Core);
                if (NativeWindow.SetTimer(Core, 2, 8, 0) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return 0;
            case 0x202: FinishPress(); return 0;
            case 0x204: rightPressed = true; return 0;
            case 0x205:
                if (rightPressed) { if (CountRequested is not null) CountRequested(false); else count = Math.Max(0, count - 1); hoverUntil = clock.Elapsed.TotalSeconds + 2; RenderFrame(); }
                rightPressed = false; return 0;
            case 0x200:
                var tracking = new NativeWindow.MouseTracking { Size = (uint)Marshal.SizeOf<NativeWindow.MouseTracking>(), Flags = 2, Hwnd = Core };
                if (!NativeWindow.TrackMouseEvent(ref tracking)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                hoverUntil = clock.Elapsed.TotalSeconds + 1.5;
                if (gesture.Phase != GesturePhase.Idle) MovePress();
                return 0;
            case 0x113:
                if (wp == 1 && visible) RenderFrame();
                if (wp == 2 && gesture.Phase != GesturePhase.Idle)
                {
                    // Temporary tracking handles the documented foreground capture limitation.
                    if ((NativeWindow.GetAsyncKeyState(1) & 0x8000) == 0) FinishPress(); else MovePress();
                }
                return 0;
            case 0x215: if (!releasing && lp != Core) EndGesture(); return 0; // capture interrupted
            case 0x1f: EndGesture(); return 0; // cancel mode
            case 0x2a3: rightPressed = false; hoverUntil = 0; return 0; // mouse left core
            case 0x2b1:
                if (wp == 7) { Locked = true; EndGesture(); NativeWindow.KillTimer(Core, 1); }
                if (wp == 8) { Locked = false; EnvironmentChanged?.Invoke(); if (visible) SetAnimationTimer(); }
                return 0;
            case 0x2e0:
                var rect = Marshal.PtrToStructure<NativeWindow.Rect>(lp);
                // The in-flight gesture keeps its original physical pointer anchor.
                pendingCenter = gesture.Phase == GesturePhase.Idle ? new((rect.Left + rect.Right)/2, (rect.Top + rect.Bottom)/2) : null;
                pendingDpi = (uint)(wp & 0xffff); return 0;
            case 0x7e: EndGesture(); center = NativeWindow.ClampToWorkArea(center, DecorationPixels/2); EnvironmentChanged?.Invoke(); RenderFrame(); return 0;
            case 0x218: if (wp == 4) EndGesture(); EnvironmentChanged?.Invoke(); if (visible) SetAnimationTimer(); return 0;
            case 0x10: Show(false); return 0;
        }
        return null;
    }
    private void MovePress()
    {
        var move = gesture.Move(NativeWindow.CursorPosition());
        if (move.PositionChanged) { center = NativeWindow.ClampToWorkArea(move.Center, DecorationPixels/2); RenderFrame(); }
    }
    private void FinishPress()
    {
        if (gesture.Phase == GesturePhase.Idle) return;
        var result = gesture.Release(NativeWindow.CursorPosition());
        if (result.PositionChanged) center = NativeWindow.ClampToWorkArea(result.Center, DecorationPixels/2);
        if (result.Clicked) { if (CountRequested is not null) CountRequested(true); else count++; }
        else if (result.PositionChanged || gesture.Phase == GesturePhase.Dragging) PositionSaved?.Invoke(center);
        EndGesture(); hoverUntil = clock.Elapsed.TotalSeconds + 2; RenderFrame();
    }
    private void EndGesture()
    {
        gesture.Cancel(); rightPressed = false; NativeWindow.KillTimer(Core, 2);
        if (NativeWindow.GetCapture() == Core)
        {
            releasing = true;
            try { NativeWindow.ReleaseCapture(); } finally { releasing = false; }
        }
    }
    public object Metrics => new { frames, meanRenderAndPresentMs = frames == 0 ? 0 : renderMilliseconds / frames,
        dpi, corePixels = CorePixels, decorationPixels = DecorationPixels, countersPersisted = committedState is not null };
    private void ReleaseSurfaces() { coreTarget?.Dispose(); decorationTarget?.Dispose(); coreSurface?.Dispose(); decorationSurface?.Dispose(); }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        EndGesture(); NativeWindow.KillTimer(Core, 1);
        NativeWindow.WTSUnRegisterSessionNotification(Core);
        NativeWindow.Destroy(Core); NativeWindow.Destroy(Decoration);
        Core = Decoration = 0;
        ReleaseSurfaces(); renderer.Dispose();
    }
}
