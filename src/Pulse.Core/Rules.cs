using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulse.Core;

public static class Rules
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower };
    public static long ParseAtoms(string text)
    {
        text=text.Trim();
        if (!Regex.IsMatch(text,@"^[0-9]+(?:\.[0-9]{1,3})?$") ||
            !decimal.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))
            throw new PulseException("invalid_amount","请输入最多三位小数的十进制数量。");
        try { return checked((long)(value*1000m)); }
        catch (OverflowException) { throw new PulseException("invalid_amount","数量超出支持范围。"); }
    }
    public static string FormatAtoms(long atoms) => (atoms/1000m).ToString("0.###",CultureInfo.InvariantCulture);
    public static void Require(bool condition, string code, string message) { if (!condition) throw new PulseException(code,message); }
    public static ActivityConfig Normalize(ActivityConfig config)
    {
        var name=config.Name.Trim(); var unit=config.Unit.Trim();
        Require(name.EnumerateRunes().Count() is >=1 and <=64,"invalid_name","名称需为1至64个字符。");
        Require(Regex.IsMatch(config.IconId,@"^[a-z0-9-]{1,64}$"),"invalid_icon","图标ID无效。");
        Require(config.Kind is "count" or "duration","invalid_kind","记录类型无效。");
        Require(unit.EnumerateRunes().Count() is >=1 and <=24,"invalid_unit","单位需为1至24个字符。");
        Require(config.StepAtoms>0,"invalid_step","每次记录量必须大于0。");
        Require(config.GroupGapMs is null or >0,"invalid_group_gap","分组间隔必须大于0。");
        Require(config.Kind!="duration" || unit=="min","invalid_duration","时长使用分钟。");
        Require(config.Breaks.B1>0 && config.Breaks.B2>config.Breaks.B1 && config.Breaks.B3>config.Breaks.B2,"invalid_breaks","三条分界须为严格递增的正数。");
        var v=config.Visual;
        Require(v.RendererVersion==1 && v.Family=="glass-orb","invalid_visual","外观格式无效。");
        Require(v.Palette.Length is >=2 and <=5,"invalid_palette","渐变需2至5个颜色。");
        Require(v.Highlight.X is >=0 and <=1 && v.Highlight.Y is >=0 and <=1,"invalid_highlight","高光位置需在球体范围内。");
        Require(v.InteriorStyle is "flowing" or "minimal","invalid_interior","内部形态无效。");
        Require(Regex.IsMatch(v.BasePreset,@"^[a-z0-9-]{1,32}$"),"invalid_preset","母版ID无效。");
        var effects=(v.Effects??new OrbEffects()) with {GlowIntensity=1,GlowScale=2};
        Require(effects.GlowIntensity is >=0 and <=2 && effects.GlowScale is >=1.2m and <=2.1m &&
            effects.HighlightIntensity is >=0 and <=2 && effects.FlowSpeed is >=0 and <=3 && effects.BreathAmplitude is >=0 and <=2,
            "invalid_effects","光晕、高光、流动或呼吸参数超出可调范围。");
        effects=effects with {GlowColor=effects.GlowColor is null?null:Color(effects.GlowColor),ParticleColor=Color(effects.ParticleColor)};
        var visual=v with { Palette=v.Palette.Select(Color).ToArray(),AccentColor=Color(v.AccentColor),
            Highlight=new(Coordinate(v.Highlight.X),Coordinate(v.Highlight.Y)),Effects=effects==new OrbEffects()?null:effects };
        return config with {Name=name,Unit=unit,Visual=visual};
    }
    private static string Color(string color)
    {
        Require(Regex.IsMatch(color,@"^#[0-9a-fA-F]{6}$"),"invalid_color","颜色需使用六位RGB格式。");
        return color.ToUpperInvariant();
    }
    private static decimal Coordinate(decimal value) => decimal.Parse(Math.Round(value,3,MidpointRounding.AwayFromZero).ToString("0.###",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);
    public static string VisualKey(VisualSpec visual)
    {
        var canonical=JsonSerializer.Serialize(new {visual.Family,visual.Palette,visual.AccentColor,visual.Highlight,visual.InteriorStyle},Json);
        var effects=(visual.Effects??new OrbEffects()) with {GlowIntensity=1,GlowScale=2};
        if(effects!=new OrbEffects())
            canonical=canonical[..^1]+",\"effects\":"+JsonSerializer.Serialize(effects,Json)+"}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
    public static int Level(long amount, HeatmapBreaks breaks) => amount==0?0:amount<breaks.B1?1:amount<breaks.B2?2:amount<breaks.B3?3:4;
    public static float Energy(long amount, HeatmapBreaks breaks) => (float)Math.Clamp((decimal)amount/breaks.B3,0,1);
    public static string[] HeatmapColors(string background, string accent)
    {
        background=Color(background); accent=Color(accent);
        var weights=new[]{0m,.25m,.45m,.70m,1m};
        return weights.Select(weight=>"#"+string.Concat(Enumerable.Range(0,3).Select(channel=>
        {
            var b=Convert.ToInt32(background.Substring(1+channel*2,2),16);
            var c=Convert.ToInt32(accent.Substring(1+channel*2,2),16);
            return ((int)Math.Round((1-weight)*b+weight*c,0,MidpointRounding.AwayFromZero)).ToString("X2");
        }))).ToArray();
    }
    public static IReadOnlyList<DailySummary> Daily(Activity activity, IReadOnlyList<DayAggregate> aggregates, DateOnly from, DateOnly to, DateOnly today)
    {
        Require(to>=from && to.DayNumber-from.DayNumber<=3660,"invalid_range","查询日期范围无效或超过十年。");
        var actual=aggregates.ToDictionary(x=>x.Date);
        return Enumerable.Range(0,to.DayNumber-from.DayNumber+1).Select(offset=>
        {
            var day=from.AddDays(offset); var has=actual.TryGetValue(day,out var value);
            var availability=has || (day>=activity.CreatedLocalDate && day<=today)?"in_scope":"not_applicable";
            return new DailySummary(day,value?.AmountAtoms??0,value?.RecordedUnits??0,availability=="in_scope"?Level(value?.AmountAtoms??0,activity.Config.Breaks):null,availability);
        }).ToArray();
    }
    public static IReadOnlyList<GroupSummary> Groups(IReadOnlyList<RecordEvent> events, long gap)
    {
        var reversed=events.Where(x=>x.ReversesId is not null).Select(x=>x.ReversesId!.Value).ToHashSet();
        var groups=new List<GroupSummary>();
        RecordEvent? previous=null; long first=0,last=0,amount=0,units=0;
        void Flush() { if(units>0) groups.Add(new(first,last,amount,units)); amount=units=0; }
        foreach(var record in events.Where(x=>x.DeltaSteps==1).OrderBy(x=>x.EventNo))
        {
            var difference=previous is null?0:checked(record.RecordedAtMs-previous.RecordedAtMs);
            if(previous is null || record.LocalDate!=previous.LocalDate || difference<0 || difference>gap) { Flush(); first=record.EventNo; }
            last=record.EventNo;
            if(!reversed.Contains(record.Id)) { amount=checked(amount+record.DeltaAmountAtoms); units=checked(units+record.DeltaUnits); }
            previous=record;
        }
        Flush(); return groups;
    }
}
