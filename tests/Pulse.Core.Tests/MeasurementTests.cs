using Pulse.Core;
using Pulse.Storage;

namespace Pulse.Core.Tests;
public sealed class MeasurementTests
{
    private sealed class Clock:IClock { public DateTimeOffset Now{get;set;}=new(2026,10,11,12,0,0,TimeSpan.FromHours(8)); }
    private static ActivityConfig Config=>new("固定份量","book","","duration","min",5000,4000,new(),VisualSpec.Default);
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void MeasurementIsLockedImmediatelyButOtherSettingsRemainEditable(bool hasHistory)
    {
        using var store=new SqliteStore(":memory:");var core=new PulseCore(store,new Clock());var a=core.Create(Guid.NewGuid(),Config);
        if(hasHistory)core.Add(a.Id,Guid.NewGuid());
        foreach(var changed in new[]{Config with{StepAtoms=1000},Config with{Kind="count",Unit="页"},Config with{Kind="count"}})
            Assert.Equal("measurement_locked",Assert.Throws<PulseException>(()=>core.Update(a.Id,changed)).Code);
        var updated=core.Update(a.Id,Config with{Name="新名称",GroupGapMs=6000,Breaks=new(1000,2000,3000)});
        Assert.Equal(5000,updated.Config.StepAtoms);Assert.Equal("新名称",updated.Config.Name);
        Assert.Equal(hasHistory?5000:0,core.Detail(a.Id,new(2026,10,11)).Summary.AmountAtoms);
    }
    [Theory]
    [InlineData("duration","min")][InlineData("count","页")][InlineData("count","自定义份")]
    public void FiveClicksStoreActualAmountsAndAllUnitsCanGroup(string kind,string unit)
    {
        using var store=new SqliteStore(":memory:");var clock=new Clock();var core=new PulseCore(store,clock);
        var a=core.Create(Guid.NewGuid(),Config with{Kind=kind,Unit=unit});
        for(var i=0;i<5;i++){if(i==2)clock.Now=clock.Now.AddSeconds(5);core.Add(a.Id,Guid.NewGuid());}
        var detail=core.Detail(a.Id,new(2026,10,11));
        Assert.Equal(25000,detail.Summary.AmountAtoms);Assert.Equal(5,detail.Summary.RecordedUnits);
        Assert.Equal(new long[]{10000,15000},detail.Groups.Select(g=>g.AmountAtoms));
        Assert.All(detail.Events,e=>Assert.Equal(5000,e.DeltaAmountAtoms));
    }
    [Fact]
    public void FractionalStepsRemainExactAndInvalidInputsFail()
    {
        using var store=new SqliteStore(":memory:");var core=new PulseCore(store,new Clock());
        var a=core.Create(Guid.NewGuid(),Config with{StepAtoms=125});for(var i=0;i<8;i++)core.Add(a.Id,Guid.NewGuid());
        Assert.Equal(1000,core.Detail(a.Id,new(2026,10,11)).Summary.AmountAtoms);
        Assert.Equal("invalid_step",Assert.Throws<PulseException>(()=>Rules.Normalize(Config with{StepAtoms=0})).Code);
        Assert.Equal("invalid_unit",Assert.Throws<PulseException>(()=>Rules.Normalize(Config with{Kind="count",Unit=" "})).Code);
    }
}
