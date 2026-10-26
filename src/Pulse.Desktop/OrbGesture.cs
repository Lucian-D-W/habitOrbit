namespace Pulse.Desktop;

public enum GesturePhase { Idle, Pressed, Dragging }
public readonly record struct ScreenPoint(int X, int Y);
public readonly record struct GestureResult(bool Clicked, bool PositionChanged, ScreenPoint Center);

public sealed class OrbGesture
{
    public GesturePhase Phase { get; private set; }
    private ScreenPoint startPointer, startCenter;
    private int halfWidth, halfHeight;
    public void Press(ScreenPoint pointer, ScreenPoint center, int dragWidth, int dragHeight)
    {
        startPointer = pointer; startCenter = center;
        halfWidth = Math.Max(1, dragWidth / 2); halfHeight = Math.Max(1, dragHeight / 2);
        Phase = GesturePhase.Pressed;
    }
    public GestureResult Move(ScreenPoint pointer)
    {
        if (Phase == GesturePhase.Idle) return new(false, false, startCenter);
        var dx = pointer.X - startPointer.X; var dy = pointer.Y - startPointer.Y;
        if (Phase == GesturePhase.Pressed && (Math.Abs(dx) > halfWidth || Math.Abs(dy) > halfHeight)) Phase = GesturePhase.Dragging;
        return new(false, Phase == GesturePhase.Dragging, new(startCenter.X + dx, startCenter.Y + dy));
    }
    public GestureResult Release(ScreenPoint pointer)
    {
        var move = Move(pointer);
        var click = Phase == GesturePhase.Pressed;
        Phase = GesturePhase.Idle;
        return move with { Clicked = click };
    }
    public void Cancel() => Phase = GesturePhase.Idle;
}
