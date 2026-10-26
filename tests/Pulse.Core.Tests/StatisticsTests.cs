using System.Text.Json;using Pulse.Core;using Pulse.Storage;
namespace Pulse.Core.Tests;
public sealed class StatisticsTests
{
 private sealed class Clock:IClock{public DateTimeOffset Now{get;set;}=new(2025,12,31,12,0,0,TimeSpan.FromHours(8));}
 [Fact] public void CrossYearStepEditsAndThresholdChangesPreserveTotalsAndCalendarFacts()
 {
  using var store=new SqliteStore(":memory:");var clock=new Clock();var core=new PulseCore(store,clock);
  var config=new ActivityConfig("读书","book-open","","count","页",500,null,new(),VisualSpec.Default);var a=core.Create(Guid.NewGuid(),config);
  core.Add(a.Id,Guid.NewGuid());clock.Now=new(2026,1,2,12,0,0,TimeSpan.FromHours(8));
  // Seed historic mixed-step data without reopening that feature in the product API.
  store.Transaction(tx=>{tx.UpdateActivity(a with{Config=config with{StepAtoms=1250,Breaks=new(100,500,1000)}});return true;});core.Add(a.Id,Guid.NewGuid());
  var context=JsonSerializer.SerializeToElement(core.StatisticsContext(a.Id,"#E8EBF2"));Assert.Equal("1750",context.GetProperty("total_atoms").GetString());Assert.Equal(new[]{2025,2026},context.GetProperty("years").EnumerateArray().Select(x=>x.GetInt32()).ToArray());Assert.Equal(5,context.GetProperty("colors").GetArrayLength());
  var matrix=core.RecentMatrix(new(2026,1,2))[a.Id];Assert.Equal(500,matrix.Single(d=>d.Date==new DateOnly(2025,12,31)).AmountAtoms);Assert.Equal(1250,matrix[^1].AmountAtoms);Assert.Equal(3,matrix.Single(d=>d.Date==new DateOnly(2025,12,31)).Level);
  var daily=core.Daily(a.Id,new(2026,1,1),new(2026,1,3));Assert.Equal("in_scope",daily[0].Availability);Assert.Equal(0,daily[0].AmountAtoms);Assert.Equal("not_applicable",daily[2].Availability);Assert.Null(daily[2].Level);
  core.Archive(a.Id,true);Assert.Empty(core.RecentMatrix(new(2026,1,2)));Assert.Equal("1750",JsonSerializer.SerializeToElement(core.StatisticsContext(a.Id,"#171B29")).GetProperty("total_atoms").GetString());
 }
}
