using Windows.UI;

namespace Pulse.Rendering;

public sealed record OrbIdentity(string Name, Color Top, Color Middle, Color Bottom, Color Accent, int Seed = 17)
{
    public double RotationMultiplier { get; init; } = 1;
    public Color[]? Palette { get; init; }
    public float HighlightX { get; init; } = .215f;
    public float HighlightY { get; init; } = .20f;
    public bool Minimal { get; init; }
    public Color? GlowColor { get; init; }
    public float GlowIntensity { get; init; } = 1;
    public float GlowScale { get; init; } = 2;
    public float HighlightIntensity { get; init; } = 1;
    public float FlowSpeed { get; init; } = 1;
    public float BreathAmplitude { get; init; } = 1;
    public Color ParticleColor { get; init; } = Rgb(255,255,255);
    public bool ShowParticles { get; init; } = true;
    public static readonly OrbIdentity Mystic = new("Mystic", Rgb(111, 110, 255), Rgb(68, 159, 255), Rgb(235, 165, 255), Rgb(161, 123, 255));
    public static readonly OrbIdentity Nature = new("Nature", Rgb(69, 181, 198), Rgb(64, 215, 181), Rgb(173, 255, 204), Rgb(67, 218, 170));
    public static readonly OrbIdentity Sunset = new("Sunset", Rgb(250, 86, 103), Rgb(242, 140, 78), Rgb(255, 245, 167), Rgb(255, 160, 75));
    public static readonly OrbIdentity[] Examples = [Mystic, Nature, Sunset];
    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
}

public readonly record struct OrbVisualState(float Energy, long FeedbackCount = 0, string? HoverText = null,
    long? GroupSatellites = null, long GroupStars = 0)
{
    public float CoreScale { get; init; } = .7f;
    public float GlowDiameterScale => CoreScale*(1.1f+1.1f*Math.Clamp(Energy,0,1));
    public float GlowStrength { get {var m=Math.Clamp(Energy,0,1);return .55f+.45f*m*m*(3-2*m);} }
    public static OrbVisualState FromMaturity(float maturity,long count=0,string? text=null,long? satellites=null,long stars=0)
    {
        var m=Math.Clamp(maturity,0,1);
        return new(m,count,text,satellites,stars){CoreScale=.7f+.1f*m};
    }
}
