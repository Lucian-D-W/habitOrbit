using Pulse.Core;
using Pulse.Storage;

namespace Pulse.Core.Tests;
public sealed class GroupPresentationTests
{
    private sealed class Clock:IClock { public DateTimeOffset Now {get;set;}=new(2026,10,11,12,0,0,TimeSpan.FromHours(8)); }
    [Fact]
    public void TrainingGroupsFollowActualQuantitiesUndoConfigurationAndDay()
    {
        using var store=new SqliteStore(":memory:");var clock=new Clock();var core=new PulseCore(store,clock);
        var a=core.Create(Guid.NewGuid(),new("训练","circle","","count","次",1000,60000,new(),VisualSpec.Default));
        var day=new DateOnly(2026,10,11);
        Assert.Equal("暂无分组\n0组 × 平均0次，共0次",GroupPresentation.Describe(core.Detail(a.Id,day).Groups,"次"));
        foreach(var size in new[]{6,7,7,10}){for(var i=0;i<size;i++)core.Add(a.Id,Guid.NewGuid());clock.Now=clock.Now.AddMilliseconds(60001);}
        Assert.Equal("6 / 7 / 7 / 10\n4组 × 平均7.5次，共30次",GroupPresentation.Describe(core.Detail(a.Id,day).Groups,"次"));
        core.Decrement(a.Id,Guid.NewGuid());Assert.Equal(9000,core.Detail(a.Id,day).Groups[^1].AmountAtoms);
        // Reproduce a legacy profile that changed step before creation-time locking.
        store.Transaction(tx=>{tx.UpdateActivity(a with{Config=a.Config with{StepAtoms=2500}});return true;});a=core.Activity(a.Id);core.Add(a.Id,Guid.NewGuid());
        Assert.Equal(2500,core.Detail(a.Id,day).Groups[^1].AmountAtoms);
        core.SetToday(a.Id,Guid.NewGuid(),day,32500,true);
        var detail=core.Detail(a.Id,day);Assert.Equal(32500,detail.Groups.Sum(g=>g.AmountAtoms));
        core.Update(a.Id,a.Config with{GroupGapMs=null});Assert.Empty(core.Detail(a.Id,day).Groups);
        core.Update(a.Id,a.Config with{GroupGapMs=999999});Assert.Single(core.Detail(a.Id,day).Groups);
        clock.Now=clock.Now.AddDays(1);core.Add(a.Id,Guid.NewGuid());Assert.Single(core.Detail(a.Id,day.AddDays(1)).Groups);
        Assert.Equal(32500,core.Detail(a.Id,day).Summary.AmountAtoms);
    }
    [Theory]
    [InlineData(60000,1)][InlineData(60001,2)]
    public void BoundaryIsStrict(long elapsed,int count)
    {
        using var store=new SqliteStore(":memory:");var clock=new Clock();var core=new PulseCore(store,clock);
        var a=core.Create(Guid.NewGuid(),new("训练","circle","","count","次",1000,60000,new(),VisualSpec.Default));
        core.Add(a.Id,Guid.NewGuid());clock.Now=clock.Now.AddMilliseconds(elapsed);core.Add(a.Id,Guid.NewGuid());
        Assert.Equal(count,core.Detail(a.Id,new(2026,10,11)).Groups.Count);
    }
    [Fact]
    public void AverageRoundsToThreePlaces()=>Assert.Equal("1 / 1 / 0.001\n3组 × 平均0.667次，共2.001次",GroupPresentation.Describe(new[]{new GroupSummary(1,1,1000,1),new(2,2,1000,1),new(3,3,1,1)},"次"));
}
