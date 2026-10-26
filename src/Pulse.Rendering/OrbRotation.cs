using System.Numerics;

namespace Pulse.Rendering;

// Extra whole-picture rotation on top of the original animation (0.04 rad/s orbit).
public static class OrbRotation
{
    public static float Angle(double seconds,double multiplier)
    {
        if(!double.IsFinite(multiplier)||multiplier<.85||multiplier>1.5)
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        if(!double.IsFinite(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
        return (float)((seconds*(.04*(multiplier-1)))%Math.Tau);
    }
    public static Matrix3x2 Transform(double seconds,double multiplier,float diameter)=>
        Matrix3x2.CreateRotation(Angle(seconds,multiplier),new Vector2(diameter/2));
}
