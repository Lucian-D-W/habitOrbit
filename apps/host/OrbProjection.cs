using Pulse.Core;
using Pulse.Rendering;
using Windows.UI;

namespace Pulse.Host;

internal static class OrbProjection
{
    public static OrbIdentity Identity(Activity a)
    {
        var v=a.Config.Visual;var p=v.Palette.Select(Color).ToArray();var effects=v.Effects??new OrbEffects();
        return new(a.Config.Name,p[0],p[p.Length/2],p[^1],Color(v.AccentColor),v.DecorativeSeed)
            {Palette=p,HighlightX=(float)v.Highlight.X,HighlightY=(float)v.Highlight.Y,Minimal=v.InteriorStyle=="minimal",
             GlowColor=effects.GlowColor is null?null:Color(effects.GlowColor),GlowIntensity=(float)effects.GlowIntensity,
             GlowScale=(float)effects.GlowScale,HighlightIntensity=(float)effects.HighlightIntensity,FlowSpeed=(float)effects.FlowSpeed,
             BreathAmplitude=(float)effects.BreathAmplitude,ParticleColor=Color(effects.ParticleColor),ShowParticles=effects.ShowParticles};
    }
    private static Color Color(string text) => Windows.UI.Color.FromArgb(255,Convert.ToByte(text.Substring(1,2),16),
        Convert.ToByte(text.Substring(3,2),16),Convert.ToByte(text.Substring(5,2),16));
    public static OrbVisualState State(Activity a,DayDetail day,DateTimeOffset now,float maturity=0)
    {
        var unit=a.Config.Kind=="duration"?"分钟":a.Config.Unit;
        var label=$"{a.Config.Name} · {Rules.FormatAtoms(day.Summary.AmountAtoms)} {unit}";
        if(a.Config.GroupGapMs is not null || day.Groups.Count>0)label+="\n"+GroupPresentation.Describe(day.Groups,unit);
        // Grouping remains a detail fact; every orb uses the renderer's ten-record aggregation.
        return OrbVisualState.FromMaturity(maturity,day.Summary.RecordedUnits,label);
    }
}
