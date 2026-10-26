using Microsoft.Data.Sqlite;
using Pulse.Core;
using Pulse.Storage;

namespace Pulse.Core.Tests;
public sealed class DayRecordingTests
{
    private sealed class Clock:IClock { public DateTimeOffset Now{get;set;}=new(2026,10,11,12,0,0,TimeSpan.FromHours(8)); }
    private static readonly DateOnly Day=new(2026,10,11);
    private static ActivityConfig Config=>new("珠串","book","","duration","min",5000,4000,new(),VisualSpec.Default);
    private static string PathForTest()=>Path.Combine(AppContext.BaseDirectory,"isolated-data",Guid.NewGuid()+".db");

    [Fact] public void BatchCreatesFivePortionsWithoutFiveFabricatedClicksAndGroupsShareFacts()
    {
        using var store=new SqliteStore(":memory:");var core=new PulseCore(store,new Clock());var a=core.Create(Guid.NewGuid(),Config);
        core.SetToday(a.Id,Guid.NewGuid(),Day,25000,true);
        var original=core.Detail(a.Id,Day);Assert.Single(original.Events);Assert.Equal(5,original.Summary.RecordedUnits);
        var value=core.Recording(a.Id,Day);Assert.Equal(5,value.Units);Assert.Equal(5000,Assert.Single(value.Runs).AmountAtoms);
        var two=core.SetGroups(a.Id,Guid.NewGuid(),Day,value.Version,[2]);
        Assert.Equal(new long[]{10000,15000},two.Groups.Select(g=>g.AmountAtoms));
        var three=core.SetGroups(a.Id,Guid.NewGuid(),Day,two.Version,[3]);
        Assert.Equal(new long[]{15000,10000},three.Groups.Select(g=>g.AmountAtoms));
        Assert.Equal(three.Groups,core.Detail(a.Id,Day).Groups);Assert.Equal(original.Events,core.Detail(a.Id,Day).Events);
        var merged=core.SetGroups(a.Id,Guid.NewGuid(),Day,three.Version,[]);
        Assert.Equal(25000,Assert.Single(merged.Groups).AmountAtoms);Assert.Equal(25000,core.Daily(a.Id,Day,Day)[0].AmountAtoms);
    }
    [Theory]
    [InlineData(2,6,true)][InlineData(5,3,false)][InlineData(6,2,false)][InlineData(8,0,false)]
    public void ReducingThreePlusFiveRemovesOnlyTailPortionsAndEmptyDividers(int removed,int expected,bool cutRemains)
    {
        using var store=new SqliteStore(":memory:");var core=new PulseCore(store,new Clock());var a=core.Create(Guid.NewGuid(),Config);
        core.SetToday(a.Id,Guid.NewGuid(),Day,40000,true);var initial=core.Recording(a.Id,Day);
        core.SetGroups(a.Id,Guid.NewGuid(),Day,initial.Version,[3]);core.SetToday(a.Id,Guid.NewGuid(),Day,expected*5000,true);
        var current=core.Recording(a.Id,Day);Assert.Equal(expected,current.Units);Assert.Equal(expected*5000,current.AmountAtoms);
        Assert.Equal(cutRemains?new long[]{3}:[],current.Cuts);Assert.Equal(current.Groups,core.Detail(a.Id,Day).Groups);
        Assert.Equal(expected,core.RecentMatrix(Day)[a.Id][^1].RecordedUnits);
        Assert.Equal((8-removed)*5000,core.Detail(a.Id,Day).Summary.AmountAtoms);
    }
    [Fact] public void DecrementRemovesOnePortionFromBatchAndReplayCannotRemoveAnother()
    {
        using var store=new SqliteStore(":memory:");var core=new PulseCore(store,new Clock());var a=core.Create(Guid.NewGuid(),Config);
        core.SetToday(a.Id,Guid.NewGuid(),Day,40000,true);var r=core.Recording(a.Id,Day);core.SetGroups(a.Id,Guid.NewGuid(),Day,r.Version,[3]);
        var request=Guid.NewGuid();var result=core.Decrement(a.Id,request);Assert.Equal(35000,result.Summary.AmountAtoms);
        core.Add(a.Id,Guid.NewGuid());Assert.Equal(result,core.Decrement(a.Id,request));Assert.Equal(8,core.Recording(a.Id,Day).Units);
        core.SetToday(a.Id,Guid.NewGuid(),Day,0,true);Assert.Equal("no_change",core.Decrement(a.Id,Guid.NewGuid()).Status);
    }
    [Fact] public void ManualRemovalSurvivesRefreshAndFutureClicksStillAutoSeparate()
    {
        using var store=new SqliteStore(":memory:");var clock=new Clock();var core=new PulseCore(store,clock);var a=core.Create(Guid.NewGuid(),Config);
        core.Add(a.Id,Guid.NewGuid());clock.Now=clock.Now.AddSeconds(5);core.Add(a.Id,Guid.NewGuid());
        var r=core.Recording(a.Id,Day);Assert.Equal(new long[]{1},r.Cuts);core.SetGroups(a.Id,Guid.NewGuid(),Day,r.Version,[]);
        core.Add(a.Id,Guid.NewGuid());Assert.Empty(core.Recording(a.Id,Day).Cuts);
        clock.Now=clock.Now.AddSeconds(5);core.Add(a.Id,Guid.NewGuid());Assert.Equal(new long[]{3},core.Recording(a.Id,Day).Cuts);
        core.Update(a.Id,Config with{GroupGapMs=null});Assert.Equal(new long[]{3},core.Recording(a.Id,Day).Cuts);
    }
    [Fact] public void StaleDraftIsRejectedAndRetryIsIdempotentEvenAfterAnotherCount()
    {
        using var store=new SqliteStore(":memory:");var core=new PulseCore(store,new Clock());var a=core.Create(Guid.NewGuid(),Config);
        core.SetToday(a.Id,Guid.NewGuid(),Day,25000,true);var r=core.Recording(a.Id,Day);var id=Guid.NewGuid();
        var saved=core.SetGroups(a.Id,id,Day,r.Version,[2]);core.Add(a.Id,Guid.NewGuid());
        var replay=core.SetGroups(a.Id,id,Day,r.Version,[2]);Assert.Equal(saved.Version,replay.Version);Assert.Equal(saved.Cuts,replay.Cuts);Assert.Equal(6,core.Recording(a.Id,Day).Units);
        Assert.Equal("recording_conflict",Assert.Throws<PulseException>(()=>core.SetGroups(a.Id,Guid.NewGuid(),Day,r.Version,[3])).Code);
        Assert.Equal("recording_conflict",Assert.Throws<PulseException>(()=>core.SetToday(a.Id,Guid.NewGuid(),Day,10000,true,r.Version)).Code);
        Assert.Equal("request_conflict",Assert.Throws<PulseException>(()=>core.SetGroups(a.Id,id,Day,r.Version,[1])).Code);
        Assert.Equal(new long[]{2},core.Recording(a.Id,Day).Cuts);
    }
    [Fact] public void FailedPartitionWriteRollsBackAmountEventsAndReceiptTogether()
    {
        var path=PathForTest();using var store=new SqliteStore(path);var core=new PulseCore(store,new Clock());var a=core.Create(Guid.NewGuid(),Config);
        core.SetToday(a.Id,Guid.NewGuid(),Day,40000,true);var r=core.Recording(a.Id,Day);core.SetGroups(a.Id,Guid.NewGuid(),Day,r.Version,[3]);
        var before=core.Detail(a.Id,Day);using var db=new SqliteConnection("Data Source="+path);db.Open();using var command=db.CreateCommand();
        command.CommandText="CREATE TRIGGER fail_partition BEFORE UPDATE ON day_partitions BEGIN SELECT RAISE(ABORT,'test failure'); END";command.ExecuteNonQuery();
        var id=Guid.NewGuid();Assert.Throws<PulseException>(()=>core.SetToday(a.Id,id,Day,10000,true));
        Assert.Equal(before.Summary,core.Detail(a.Id,Day).Summary);Assert.Equal(before.Events,core.Detail(a.Id,Day).Events);Assert.Equal(before.Recording!.Cuts,core.Recording(a.Id,Day).Cuts);
        Assert.Null(store.Transaction(tx=>tx.GetReceipt(id)));
        command.CommandText="DROP TRIGGER fail_partition";command.ExecuteNonQuery();core.SetToday(a.Id,id,Day,10000,true);Assert.Equal(2,core.Recording(a.Id,Day).Units);
    }
    [Fact] public void ReopeningKeepsPartitionsAndHugeCountsStayCompressedAndExact()
    {
        var path=PathForTest();Guid activity;string version;var request=Guid.NewGuid();const long total=9007199254740993000;
        using(var store=new SqliteStore(path))
        {
            var core=new PulseCore(store,new Clock());activity=core.Create(Guid.NewGuid(),Config with{StepAtoms=1000}).Id;
            core.SetToday(activity,Guid.NewGuid(),Day,total,true);var r=core.Recording(activity,Day);Assert.Single(r.Runs);version=r.Version;
            core.SetGroups(activity,request,Day,version,[3,9007199254740992]);
        }
        using(var store=new SqliteStore(path))
        {
            var core=new PulseCore(store,new Clock());var r=core.Recording(activity,Day);Assert.Equal(total,r.AmountAtoms);Assert.Equal(9007199254740993,r.Units);
            Assert.Equal(new long[]{3,9007199254740992},r.Cuts);Assert.Equal(r.Cuts,core.SetGroups(activity,request,Day,version,r.Cuts).Cuts);
            Assert.Equal(total,r.Groups.Sum(g=>g.AmountAtoms));
        }
    }
    [Fact] public void InvalidPortionsDividersDateAndArchivedWritesDoNotChangeFacts()
    {
        using var store=new SqliteStore(":memory:");var core=new PulseCore(store,new Clock());var a=core.Create(Guid.NewGuid(),Config);
        Assert.Equal("invalid_portion",Assert.Throws<PulseException>(()=>core.SetToday(a.Id,Guid.NewGuid(),Day,23000,true)).Code);
        core.SetToday(a.Id,Guid.NewGuid(),Day,25000,true);var r=core.Recording(a.Id,Day);
        foreach(var cuts in new[]{new long[]{0},[5],[2,2],[3,1]})Assert.Equal("invalid_divider",Assert.Throws<PulseException>(()=>core.SetGroups(a.Id,Guid.NewGuid(),Day,r.Version,cuts)).Code);
        Assert.Equal("date_changed",Assert.Throws<PulseException>(()=>core.SetGroups(a.Id,Guid.NewGuid(),Day.AddDays(-1),r.Version,[2])).Code);
        core.Archive(a.Id,true);Assert.Equal("archived",Assert.Throws<PulseException>(()=>core.SetGroups(a.Id,Guid.NewGuid(),Day,r.Version,[2])).Code);
        Assert.Equal(r.Version,core.Recording(a.Id,Day).Version);
    }
}
