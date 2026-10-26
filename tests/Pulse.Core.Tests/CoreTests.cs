using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pulse.Core;
using Pulse.Storage;

namespace Pulse.Core.Tests;

public sealed class CoreTests
{
    private sealed class Clock : IClock { public DateTimeOffset Now {get;set;}=new(2026,10,4,12,0,0,TimeSpan.FromHours(8)); }
    private sealed class Fixture : IDisposable
    {
        public readonly Clock Clock=new();
        public readonly SqliteStore Store;
        public readonly PulseCore Core;
        public Fixture(string path=":memory:") { Store=new(path); Core=new(Store,Clock); }
        public Activity Create(ActivityConfig? config=null)=>Core.Create(Guid.NewGuid(),config??Config());
        public void Dispose()=>Store.Dispose();
    }
    private static ActivityConfig Config(long step=1000,long? gap=null)=>new("阅读","book-open","","count","页",step,gap,new(),VisualSpec.Default);
    private static string PathForTest()=>Path.Combine(AppContext.BaseDirectory,"isolated-data",Guid.NewGuid()+".db");
    private static DateOnly Today=>new(2026,10,4);

    [Theory]
    [InlineData("1",1000)][InlineData("0.5",500)][InlineData("5.125",5125)][InlineData("0005.001",5001)]
    public void DecimalInputIsExact(string input,long expected)=>Assert.Equal(expected,Rules.ParseAtoms(input));
    [Theory]
    [InlineData("1.0000")][InlineData("0.0001")][InlineData("-1")][InlineData("1e3")][InlineData("NaN")][InlineData("1,25")][InlineData("9223372036854775.808")]
    public void DecimalInputDoesNotRoundOrOverflow(string input)=>Assert.Throws<PulseException>(()=>Rules.ParseAtoms(input));
    [Fact]
    public void A01FiveAddsAreFiveFacts()
    {
        using var f=new Fixture(); var a=f.Create();
        for(var i=0;i<5;i++)f.Core.Add(a.Id,Guid.NewGuid());
        var detail=f.Core.Detail(a.Id,Today);
        Assert.Equal(5000,detail.Summary.AmountAtoms); Assert.Equal(5,detail.Summary.RecordedUnits);
        Assert.Equal(5,detail.Events.Count); Assert.All(detail.Events,x=>Assert.Equal(1,x.DeltaSteps));
    }
    [Fact]
    public void A04SecondUndoIsNoChange()
    {
        using var f=new Fixture(); var a=f.Create(); f.Core.Add(a.Id,Guid.NewGuid());
        Assert.Equal("changed",f.Core.Decrement(a.Id,Guid.NewGuid()).Status);
        var result=f.Core.Decrement(a.Id,Guid.NewGuid());
        Assert.Equal("no_change",result.Status); Assert.Null(result.Event); Assert.Equal(0,result.Summary.AmountAtoms);
        Assert.Equal(2,f.Core.Detail(a.Id,Today).Events.Count);
    }
    [Fact]
    public void A05ToA07StepSnapshotAndUndoOriginalAmount()
    {
        using var f=new Fixture(); var a=f.Create(Config(5000) with{Kind="duration",Unit="min"});
        for(var i=0;i<4;i++)f.Core.Add(a.Id,Guid.NewGuid());
        // Seed an old-version step change: current product updates must reject this.
        f.Store.Transaction(tx=>{tx.UpdateActivity(a with{Config=a.Config with{StepAtoms=10000}});return true;});
        Assert.Equal(20000,f.Core.Detail(a.Id,Today).Summary.AmountAtoms);
        Assert.Equal(30000,f.Core.Add(a.Id,Guid.NewGuid()).Summary.AmountAtoms);
        Assert.Equal(-10000,f.Core.Decrement(a.Id,Guid.NewGuid()).Event!.DeltaAmountAtoms);
        Assert.Equal(-5000,f.Core.Decrement(a.Id,Guid.NewGuid()).Event!.DeltaAmountAtoms);
        Assert.Equal(15000,f.Core.Detail(a.Id,Today).Summary.AmountAtoms);
    }
    [Theory]
    [InlineData(4000,1)][InlineData(4001,2)][InlineData(-1,2)]
    public void A08A09GroupBoundaries(long gap,int expected)
    {
        using var f=new Fixture(); var a=f.Create(Config(gap:4000));
        f.Core.Add(a.Id,Guid.NewGuid()); f.Clock.Now=f.Clock.Now.AddMilliseconds(gap); f.Core.Add(a.Id,Guid.NewGuid());
        Assert.Equal(expected,f.Core.Detail(a.Id,Today).Groups.Count);
    }
    [Fact]
    public void A10ReversalDoesNotRepartitionOriginalPositiveSequence()
    {
        using var f=new Fixture(); var a=f.Create(Config(gap:4000));
        f.Core.Add(a.Id,Guid.NewGuid()); f.Clock.Now=f.Clock.Now.AddSeconds(3); f.Core.Add(a.Id,Guid.NewGuid());
        f.Core.Decrement(a.Id,Guid.NewGuid()); f.Clock.Now=f.Clock.Now.AddSeconds(3); f.Core.Add(a.Id,Guid.NewGuid());
        var group=Assert.Single(f.Core.Detail(a.Id,Today).Groups);
        Assert.Equal(2,group.RecordedUnits); Assert.Equal(2000,group.AmountAtoms);
    }
    [Fact]
    public void A11UnequalGroupsKeepTheirAmounts()
    {
        using var f=new Fixture(); var a=f.Create(Config(gap:4000));
        foreach(var size in new[]{5,5,4})
        {
            for(var i=0;i<size;i++) { f.Core.Add(a.Id,Guid.NewGuid()); f.Clock.Now=f.Clock.Now.AddMilliseconds(100); }
            f.Clock.Now=f.Clock.Now.AddSeconds(5);
        }
        var detail=f.Core.Detail(a.Id,Today);
        Assert.Equal(new long[]{5,5,4},detail.Groups.Select(x=>x.RecordedUnits)); Assert.Equal(14000,detail.Summary.AmountAtoms);
    }
    [Fact]
    public void A12SameUuidReplaysOriginalResponseAndRejectsConflict()
    {
        using var f=new Fixture(); var a=f.Create(); var id=Guid.NewGuid(); var original=f.Core.Add(a.Id,id);
        f.Core.Add(a.Id,Guid.NewGuid());
        Assert.Equal(original,f.Core.Add(a.Id,id)); Assert.Equal(2,f.Core.Detail(a.Id,Today).Events.Count);
        Assert.Equal("request_conflict",Assert.Throws<PulseException>(()=>f.Core.Decrement(a.Id,id)).Code);
        var other=f.Create(Config() with{Visual=VisualSpec.Default with{AccentColor="#00FF00"}});
        Assert.Equal("request_conflict",Assert.Throws<PulseException>(()=>f.Core.Add(other.Id,id)).Code);
    }
    [Theory]
    [InlineData(0,0)][InlineData(4000,1)][InlineData(5000,2)][InlineData(9000,2)][InlineData(10000,3)][InlineData(19000,3)][InlineData(20000,4)]
    public void A13ThresholdEndpoints(long amount,int expected)=>Assert.Equal(expected,Rules.Level(amount,new()));
    [Fact]
    public void A14ThresholdChangesDoNotRewriteEvents()
    {
        using var f=new Fixture(); var a=f.Create(Config(5000)); f.Core.Add(a.Id,Guid.NewGuid());
        var before=f.Core.Detail(a.Id,Today);
        f.Core.Update(a.Id,a.Config with{Breaks=new(10000,20000,30000)});
        var after=f.Core.Detail(a.Id,Today);
        Assert.Equal(2,before.Summary.Level); Assert.Equal(1,after.Summary.Level);
        Assert.Equal(before.Events,after.Events); Assert.Equal(before.Summary.AmountAtoms,after.Summary.AmountAtoms);
    }
    [Fact]
    public void A16SeedAndPresetDoNotCreateAnotherVisibleIdentity()
    {
        using var f=new Fixture(); f.Create();
        var duplicate=Config() with{Visual=VisualSpec.Default with{DecorativeSeed=42,BasePreset="custom"}};
        Assert.Equal("visual_duplicate",Assert.Throws<PulseException>(()=>f.Create(duplicate)).Code);
        Assert.Single(f.Core.List());
    }
    [Fact]
    public void ColorCaseAndHighlightPrecisionNormalize()
    {
        var first=Rules.Normalize(Config());
        var second=Rules.Normalize(Config() with{Visual=VisualSpec.Default with{Palette=["#6b5ce7","#508fef","#df96f0"],Highlight=new(.3004m,.2504m)}});
        Assert.Equal(Rules.VisualKey(first.Visual),Rules.VisualKey(second.Visual));
        Assert.Equal(new[]{"#000000","#404040","#737373","#B3B3B3","#FFFFFF"},Rules.HeatmapColors("#000000","#FFFFFF"));
    }
    [Fact]
    public void MidnightStartsEmptyAndCannotUndoYesterday()
    {
        using var f=new Fixture(); var a=f.Create(Config(gap:4000)); f.Clock.Now=new(2026,10,4,23,59,59,TimeSpan.FromHours(8));
        f.Core.Add(a.Id,Guid.NewGuid()); f.Clock.Now=f.Clock.Now.AddSeconds(2);
        Assert.Equal("no_change",f.Core.Decrement(a.Id,Guid.NewGuid()).Status);
        f.Core.Add(a.Id,Guid.NewGuid());
        Assert.Equal(1000,f.Core.Detail(a.Id,Today).Summary.AmountAtoms);
        Assert.Equal(1000,f.Core.Detail(a.Id,Today.AddDays(1)).Summary.AmountAtoms);
        Assert.Single(f.Core.Detail(a.Id,Today).Groups); Assert.Single(f.Core.Detail(a.Id,Today.AddDays(1)).Groups);
    }
    [Fact]
    public void A30A31FactsRemainVisibleAfterTimezoneChanges()
    {
        using var f=new Fixture(); var a=f.Create(); f.Core.Add(a.Id,Guid.NewGuid());
        f.Clock.Now=new(2026,10,3,23,0,0,TimeSpan.FromHours(-8));
        var days=f.Core.Daily(a.Id,Today.AddDays(-1),Today.AddDays(1));
        Assert.Equal("not_applicable",days[0].Availability); Assert.Null(days[0].Level);
        Assert.Equal("in_scope",days[1].Availability); Assert.Equal(1000,days[1].AmountAtoms);
        Assert.Equal("not_applicable",days[2].Availability);
        Assert.Equal(480,f.Core.Detail(a.Id,Today).Events[0].UtcOffsetMinutes);
    }
    [Fact]
    public void FullyReversedDateIsStillAnExistingFact()
    {
        using var f=new Fixture(); var a=f.Create(); f.Core.Add(a.Id,Guid.NewGuid()); f.Core.Decrement(a.Id,Guid.NewGuid());
        f.Clock.Now=f.Clock.Now.AddDays(-2);
        var day=Assert.Single(f.Core.Daily(a.Id,Today,Today));
        Assert.Equal("in_scope",day.Availability); Assert.Equal(0,day.AmountAtoms); Assert.Equal(0,day.Level);
    }
    [Fact]
    public void ArchiveRetainsHistoryAndRestoreAllowsRecording()
    {
        using var f=new Fixture(); var a=f.Create(); f.Core.Add(a.Id,Guid.NewGuid()); f.Core.Archive(a.Id,true);
        Assert.Empty(f.Core.List()); Assert.Single(f.Core.List(true));
        Assert.Equal("archived",Assert.Throws<PulseException>(()=>f.Core.Add(a.Id,Guid.NewGuid())).Code);
        f.Core.Archive(a.Id,false); Assert.Equal(2000,f.Core.Add(a.Id,Guid.NewGuid()).Summary.AmountAtoms);
    }
    [Fact]
    public void HistoryLocksKindAndUnitEvenWhenFullyReversed()
    {
        using var f=new Fixture(); var a=f.Create(); f.Core.Add(a.Id,Guid.NewGuid()); f.Core.Decrement(a.Id,Guid.NewGuid());
        Assert.Equal("measurement_locked",Assert.Throws<PulseException>(()=>f.Core.Update(a.Id,a.Config with{Unit="次"})).Code);
    }
    [Fact]
    public void CreationUuidIsIdempotentButParametersMustMatch()
    {
        using var f=new Fixture(); var id=Guid.NewGuid(); var a=f.Core.Create(id,Config());
        Assert.Equal(a.Id,f.Core.Create(id,Config()).Id);
        Assert.Equal("request_conflict",Assert.Throws<PulseException>(()=>f.Core.Create(id,Config(2000))).Code);
    }
    [Fact]
    public void NoChangeUuidDoesNotUndoLaterRecordsEvenAfterReopen()
    {
        var path=PathForTest(); var activity=Guid.NewGuid(); var command=Guid.NewGuid();
        using(var f=new Fixture(path)) { f.Core.Create(activity,Config()); Assert.Equal("no_change",f.Core.Decrement(activity,command).Status); f.Core.Add(activity,Guid.NewGuid()); }
        using(var f=new Fixture(path))
        {
            Assert.Equal("no_change",f.Core.Decrement(activity,command).Status);
            Assert.Equal(1000,f.Core.Detail(activity,Today).Summary.AmountAtoms);
            Assert.Equal("request_conflict",Assert.Throws<PulseException>(()=>f.Core.Add(activity,command)).Code);
        }
    }
    [Fact]
    public void ReopenRestoresEventsSummariesAndReceiptExactly()
    {
        var path=PathForTest(); var activity=Guid.NewGuid(); var command=Guid.NewGuid(); string snapshot;
        using(var f=new Fixture(path)) { f.Core.Create(activity,Config(gap:4000)); f.Core.Add(activity,command); snapshot=JsonSerializer.Serialize(f.Core.Detail(activity,Today),Rules.Json); }
        using(var f=new Fixture(path)) { Assert.Equal(snapshot,JsonSerializer.Serialize(f.Core.Detail(activity,Today),Rules.Json)); f.Core.Add(activity,command); Assert.Single(f.Core.Detail(activity,Today).Events); }
    }
    [Fact]
    public void ParallelDuplicateCommandsCommitExactlyOnce()
    {
        using var f=new Fixture(); var a=f.Create(); var id=Guid.NewGuid();
        Parallel.For(0,30,_=>f.Core.Add(a.Id,id)); Assert.Single(f.Core.Detail(a.Id,Today).Events);
        Parallel.For(0,30,_=>f.Core.Add(a.Id,Guid.NewGuid())); Assert.Equal(31000,f.Core.Detail(a.Id,Today).Summary.AmountAtoms);
    }
    [Fact]
    public void FailedTransactionLeavesNoEventOrReceipt()
    {
        using var f=new Fixture(); var a=f.Create(); var id=Guid.NewGuid();
        Assert.Throws<IOException>(()=>f.Store.Transaction<int>(tx=> { tx.InsertEvent(new(0,id,a.Id,0,Today,0,1,1000,null)); throw new IOException("Injected failure"); }));
        Assert.Empty(f.Core.Detail(a.Id,Today).Events); Assert.Equal("changed",f.Core.Add(a.Id,id).Status);
    }
    [Fact]
    public void RecentMatrixUsesRollingDatesAndExcludesArchived()
    {
        using var f=new Fixture(); var a=f.Create(); f.Core.Add(a.Id,Guid.NewGuid());
        var other=f.Create(Config() with{Visual=VisualSpec.Default with{AccentColor="#FF0000"}}); f.Core.Archive(other.Id,true);
        var row=Assert.Single(f.Core.RecentMatrix(Today)); Assert.Equal(a.Id,row.Key);
        Assert.Equal(7,row.Value.Count); Assert.Equal(Today.AddDays(-6),row.Value[0].Date); Assert.Equal(1000,row.Value[^1].AmountAtoms);
    }
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(99)]
    public void RefusingAnotherDatabaseDoesNotChangeItsBytes(int version)
    {
        var path=PathForTest(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString()))
        {
            connection.Open(); using var command=connection.CreateCommand();
            command.CommandText=$"CREATE TABLE unrelated(value TEXT); INSERT INTO unrelated VALUES('keep'); PRAGMA user_version={version};"; command.ExecuteNonQuery();
        }
        var original=File.ReadAllBytes(path);
        Assert.Throws<PulseException>(()=>new SqliteStore(path));
        Assert.Equal(original,File.ReadAllBytes(path));
    }
    [Fact]
    public void NumericOverflowRollsBackAdditionAndKeepsUndoPossible()
    {
        using var f=new Fixture(); var a=f.Create(Config(long.MaxValue)); f.Core.Add(a.Id,Guid.NewGuid());
        Assert.Equal("numeric_limit",Assert.Throws<PulseException>(()=>f.Core.Add(a.Id,Guid.NewGuid())).Code);
        Assert.Single(f.Core.Detail(a.Id,Today).Events);
        Assert.Equal(0,f.Core.Decrement(a.Id,Guid.NewGuid()).Summary.AmountAtoms);
    }
}
