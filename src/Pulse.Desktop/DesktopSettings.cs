using System.Text.Json;

namespace Pulse.Desktop;

public sealed record OrbPlacement(bool Visible, string Monitor, double CenterXDip, double CenterYDip);
public sealed record DesktopSettings(int Version, string AnimationMode, bool StartWithWindows, string LastView,
    Dictionary<Guid,OrbPlacement> Orbs)
{
    public Dictionary<Guid,double> OrbRotationMultipliers { get; init; } = new();
    public static DesktopSettings Default => new(1,"normal",false,"gallery",new());
}
public sealed class SettingsFile(string path, Action<Exception> report)
{
    private static readonly JsonSerializerOptions Json = new() {PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,WriteIndented=true};
    public DesktopSettings Load()
    {
        if (!File.Exists(path)) return DesktopSettings.Default;
        try
        {
            var settings=JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllBytes(path),Json);
            if(settings is null || settings.Version!=1 || settings.Orbs is null ||
                settings.AnimationMode is not ("normal" or "economy") ||
                settings.Orbs.Values.Any(p=>p is null || p.Monitor is null || !double.IsFinite(p.CenterXDip) || !double.IsFinite(p.CenterYDip)))
                throw new InvalidDataException("本机布局配置无效，将使用默认布局；数据库保持不变。");
            // Rotation is independent of placement; a missing/invalid optional map must not reset positions.
            if(settings.OrbRotationMultipliers is null)settings=settings with {OrbRotationMultipliers=new()};
            foreach(var id in settings.OrbRotationMultipliers.Where(p=>!double.IsFinite(p.Value)||p.Value<.85||p.Value>1.5).Select(p=>p.Key).ToArray())
                settings.OrbRotationMultipliers.Remove(id);
            return settings;
        }
        catch(Exception e) when(e is JsonException or IOException or InvalidDataException) {report(e);return DesktopSettings.Default;}
    }
    public void Save(DesktopSettings settings)
    {
        var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary=full+".tmp";
        using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))
        {JsonSerializer.Serialize(stream,settings,Json);stream.Flush(true);}
        if(File.Exists(full)) File.Replace(temporary,full,null);else File.Move(temporary,full);
    }
}
