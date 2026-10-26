using Microsoft.Data.Sqlite;
using Pulse.Core;
using Pulse.Storage;

namespace Pulse.Core.Tests;
public sealed class DeleteTests
{
    [Fact]
    public void ConfirmationAndArchiveAreRequiredAndOnlyTargetHistoryIsDeleted()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"isolated-data",Guid.NewGuid()+".db");
        using var store=new SqliteStore(path);var core=new PulseCore(store,new SystemClock());
        var config=new ActivityConfig("删除测试","book-open","","count","页",1000,null,new(),VisualSpec.Default);
        var a=core.Create(Guid.NewGuid(),config);var b=core.Create(Guid.NewGuid(),config with{Name="保留测试",Visual=config.Visual with{AccentColor="#123456"}});
        core.Add(a.Id,Guid.NewGuid());core.Add(a.Id,Guid.NewGuid());core.Decrement(a.Id,Guid.NewGuid());core.Decrement(a.Id,Guid.NewGuid());
        core.Add(b.Id,Guid.NewGuid());var date=DateOnly.FromDateTime(DateTime.Now);var other=core.Detail(b.Id,date);
        Assert.Equal("not_archived",Assert.Throws<PulseException>(()=>core.Delete(a.Id,true)).Code);
        core.Archive(a.Id,true);
        Assert.Equal("confirmation_required",Assert.Throws<PulseException>(()=>core.Delete(a.Id,false)).Code);
        Assert.Equal(4,core.Detail(a.Id,date).Events.Count);
        core.Delete(a.Id,true);
        Assert.Equal("not_found",Assert.Throws<PulseException>(()=>core.Activity(a.Id)).Code);
        Assert.Single(core.List(true));Assert.Equal(other.Summary,core.Detail(b.Id,date).Summary);
        Assert.Equal(other.Events.ToArray(),core.Detail(b.Id,date).Events.ToArray());
        using var db=new SqliteConnection("Data Source="+path);db.Open();
        foreach(var table in new[]{"activities","events","command_receipts"})
        {
            using var query=db.CreateCommand();query.CommandText=$"SELECT COUNT(*) FROM {table} WHERE {(table=="activities"?"id":"activity_id")}=$id";query.Parameters.AddWithValue("$id",a.Id.ToString());Assert.Equal(0L,query.ExecuteScalar());
        }
        core.Archive(b.Id,true);core.Archive(b.Id,false);Assert.Equal(other.Summary,core.Detail(b.Id,date).Summary);
    }
}
