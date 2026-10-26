namespace Pulse.Core;

public sealed class PulseException(string code, string message) : Exception(message) { public string Code { get; } = code; }
public interface IClock { DateTimeOffset Now { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset Now => DateTimeOffset.Now; }
public sealed record HeatmapBreaks(long B1 = 5000, long B2 = 10000, long B3 = 20000);
public sealed record Highlight(decimal X = .30m, decimal Y = .25m);
public sealed record OrbEffects(string? GlowColor = null, decimal GlowIntensity = 1, decimal GlowScale = 2,
    decimal HighlightIntensity = 1, decimal FlowSpeed = 1, decimal BreathAmplitude = 1,
    string ParticleColor = "#FFFFFF", bool ShowParticles = true);
public sealed record VisualSpec(int RendererVersion, string Family, string BasePreset, string[] Palette,
    string AccentColor, Highlight Highlight, string InteriorStyle, int DecorativeSeed)
{
    public OrbEffects? Effects { get; init; }
    public static VisualSpec Default => new(1,"glass-orb","mystic",["#6B5CE7","#508FEF","#DF96F0"],"#8B7AEC",new(),"flowing",1842);
}
public sealed record ActivityConfig(string Name, string IconId, string Description, string Kind, string Unit,
    long StepAtoms, long? GroupGapMs, HeatmapBreaks Breaks, VisualSpec Visual);
public sealed record Activity(Guid Id, ActivityConfig Config, string VisualKey, long CreatedAtMs,
    DateOnly CreatedLocalDate, long UpdatedAtMs, long? ArchivedAtMs);
public sealed record RecordEvent(long EventNo, Guid Id, Guid ActivityId, long RecordedAtMs, DateOnly LocalDate,
    int UtcOffsetMinutes, int DeltaSteps, long DeltaAmountAtoms, Guid? ReversesId)
{ public long DeltaUnits {get;init;}=DeltaSteps; }
public sealed record DayAggregate(Guid ActivityId, DateOnly Date, long AmountAtoms, long RecordedUnits);
public sealed record DailySummary(DateOnly Date, long AmountAtoms, long RecordedUnits, int? Level, string Availability);
public sealed record GroupSummary(long FirstEventNo, long LastEventNo, long AmountAtoms, long RecordedUnits);
public sealed record DayDetail(DailySummary Summary, IReadOnlyList<RecordEvent> Events, IReadOnlyList<GroupSummary> Groups)
{ public DayRecording? Recording {get;init;} }
public sealed record CountResult(string Status, RecordEvent? Event, DailySummary Summary)
{ public long? RequestedAmountAtoms {get;init;} }
public sealed record CommandReceipt(Guid Id, Guid ActivityId, string Operation, CountResult Result);

public interface IStore : IDisposable
{
    T Transaction<T>(Func<ITransaction,T> action);
}
public interface ITransaction
{
    Activity? GetActivity(Guid id);
    IReadOnlyList<Activity> ListActivities(bool includeArchived);
    bool HasEvents(Guid activityId);
    void InsertActivity(Activity activity);
    void UpdateActivity(Activity activity);
    void DeleteActivity(Guid activityId);
    CommandReceipt? GetReceipt(Guid id);
    void InsertReceipt(CommandReceipt receipt);
    RecordEvent? LatestUnreversed(Guid activityId, DateOnly day);
    RecordEvent InsertEvent(RecordEvent record);
    IReadOnlyList<RecordEvent> DayEvents(Guid activityId, DateOnly day);
    IReadOnlyList<DayAggregate> DailyAggregates(Guid activityId, DateOnly from, DateOnly to);
    IReadOnlyList<DayAggregate> MatrixAggregates(DateOnly from, DateOnly to);
    DayPartition? GetPartition(Guid activityId,DateOnly day);
    void SavePartition(Guid activityId,DateOnly day,DayPartition partition);
    GroupReceipt? GetGroupReceipt(Guid id);
    void InsertGroupReceipt(GroupReceipt receipt);
}
