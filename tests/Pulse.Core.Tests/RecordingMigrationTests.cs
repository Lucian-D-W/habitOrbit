using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pulse.Core;
using Pulse.Storage;

namespace Pulse.Core.Tests;
public sealed class RecordingMigrationTests
{
    private sealed class Clock:IClock { public DateTimeOffset Now=>new(2026,10,11,12,0,0,TimeSpan.FromHours(8)); }
    private static readonly DateOnly Day=new(2026,10,11);
    private static string PathForTest()=>Path.Combine(AppContext.BaseDirectory,"isolated-data",Guid.NewGuid()+".db");
    private static string Facts(string path)
    {
        using var db=new SqliteConnection("Data Source="+path+";Mode=ReadOnly");db.Open();var result=new List<object>();
        foreach(var sql in new[]{"SELECT * FROM activities ORDER BY id","SELECT event_no,id,activity_id,recorded_at_ms,local_date,utc_offset_minutes,delta_steps,delta_amount_atoms,reverses_id FROM events ORDER BY event_no","SELECT * FROM command_receipts ORDER BY id","SELECT * FROM gallery_layout ORDER BY id"})
        {
            using var cmd=db.CreateCommand();cmd.CommandText=sql;using var rows=cmd.ExecuteReader();var table=new List<object[]>();
            while(rows.Read()){var row=new object[rows.FieldCount];rows.GetValues(row);table.Add(row);}result.Add(table);
        }
        return JsonSerializer.Serialize(result);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void LegacyBatchReceiptsExpandOnlyKnownBulkRecordsWithoutChangingExistingFacts(bool reversed)
    {
        var path=PathForTest();Guid activity;
        using(var store=new SqliteStore(path))
        {
            var core=new PulseCore(store,new Clock());activity=core.Create(Guid.NewGuid(),new("旧数据","book","","duration","min",5000,4000,new(),VisualSpec.Default)).Id;
            core.Add(activity,Guid.NewGuid());
            store.Transaction(tx=>
            {
                var batch=tx.InsertEvent(new(0,Guid.NewGuid(),activity,1791691200500,Day,480,1,20000,null));
                tx.InsertReceipt(new(Guid.NewGuid(),activity,"set_today",new("changed",batch,new(Day,25000,2,4,"in_scope")){RequestedAmountAtoms=25000}));
                if(reversed)tx.InsertEvent(new(0,Guid.NewGuid(),activity,1791691200600,Day,480,-1,-20000,batch.Id));return true;
            });
        }
        using(var db=new SqliteConnection("Data Source="+path))
        {
            db.Open();using var cmd=db.CreateCommand();cmd.CommandText="ALTER TABLE events DROP COLUMN delta_units; DROP TABLE day_partitions; DROP TABLE group_receipts; PRAGMA user_version=2";cmd.ExecuteNonQuery();
        }
        var before=Facts(path);
        using(var store=new SqliteStore(path))
        {
            Assert.Equal(before,Facts(path));var core=new PulseCore(store,new Clock());var r=core.Recording(activity,Day);
            Assert.Equal(reversed?1:5,r.Units);Assert.Equal(reversed?5000:25000,r.AmountAtoms);
            Assert.Equal(r.Units,core.Detail(activity,Day).Summary.RecordedUnits);
            Assert.Equal(reversed?2:1,core.Detail(activity,Day).Events.Count(e=>e.DeltaSteps!=1 || e.DeltaUnits>1));
        }
        Assert.True(File.Exists(path+".before-schema-3.db"));
    }
    [Fact] public void IsolatedProfileCopyKeepsOriginalTablesAndPassesIntegrity()
    {
        if(Environment.GetEnvironmentVariable("PULSE_RECORDING_COPY") is not string path)return;
        var before=Facts(path);using(var store=new SqliteStore(path)){Assert.Equal(before,Facts(path));}
        using var db=new SqliteConnection("Data Source="+path+";Mode=ReadOnly");db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="PRAGMA user_version";Assert.Equal(3L,cmd.ExecuteScalar());
        cmd.CommandText="PRAGMA integrity_check";Assert.Equal("ok",cmd.ExecuteScalar());
        cmd.CommandText="PRAGMA foreign_key_check";using var rows=cmd.ExecuteReader();Assert.False(rows.Read());
    }
}
