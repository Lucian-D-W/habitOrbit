using Pulse.Core;
using Pulse.Storage;

namespace Pulse.Core.Tests;

public sealed class MaturityTests
{
    private static readonly DateOnly Start=new(2026,1,1);
    private static DayAggregate Day(int offset,long units=1)=>new(Guid.Empty,Start.AddDays(offset),units*1000,units);
    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(4)][InlineData(7)]
    public void TwoDayRhythmHoldsUntilEightDays(int pause)
    {
        var history=new[]{Day(0),Day(2),Day(4)};
        Assert.Equal(.142625,Maturity.Evaluate(history,Start.AddDays(4+pause)).Value,8);
        Assert.Equal(0,Maturity.Evaluate(history,Start.AddDays(12)).Value);
        Assert.Equal(.05,Maturity.Evaluate(history.Append(Day(12)),Start.AddDays(12)).Value,8);
        Assert.Equal(2,Maturity.Evaluate(history.Append(Day(12)),Start.AddDays(12)).TypicalInterval);
    }
    [Fact]
    public void EffectiveDatesNotClickCountsAndFutureFactsDriveGrowth()
    {
        Assert.Equal(.0975,Maturity.Evaluate([Day(0,100),Day(1,10),Day(2,0),Day(9)],Start.AddDays(2)).Value,8);
        var mature=Maturity.Evaluate(Enumerable.Range(0,1000).Select(i=>Day(i)),Start.AddDays(999));
        Assert.InRange(.7+.1*mature.Value,.7,.8);
        Assert.True(mature.Value>.999);
    }
    private sealed class Clock:IClock {public DateTimeOffset Now{get;set;}=new(2026,1,1,12,0,0,TimeSpan.FromHours(8));}
    [Fact]
    public void UndoBackfillAndReopenReconstructTheSameEffectiveHistory()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"isolated-data",Guid.NewGuid()+".db");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var clock=new Clock();Guid id;
        using(var store=new SqliteStore(path))
        {
            var core=new PulseCore(store,clock);
            id=core.Create(Guid.NewGuid(),new("阅读","book-open","","count","页",1000,null,new(),VisualSpec.Default)).Id;
            core.Add(id,Guid.NewGuid());core.Add(id,Guid.NewGuid());
            Assert.Equal(.05,core.Growth(id,Start).Value,8);
            clock.Now=clock.Now.AddDays(2);core.Add(id,Guid.NewGuid());
            Assert.Equal(.0975,core.Growth(id,Start.AddDays(2)).Value,8);
            core.Decrement(id,Guid.NewGuid());Assert.Equal(.05,core.Growth(id,Start.AddDays(2)).Value,8);
            clock.Now=clock.Now.AddDays(-1);core.Add(id,Guid.NewGuid());
            Assert.Equal(.0975,core.Growth(id,Start.AddDays(2)).Value,8);
        }
        using(var reopened=new SqliteStore(path))Assert.Equal(.0975,new PulseCore(reopened,clock).Growth(id,Start.AddDays(2)).Value,8);
    }
}
