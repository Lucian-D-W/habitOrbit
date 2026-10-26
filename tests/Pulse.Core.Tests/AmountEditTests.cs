using Microsoft.Data.Sqlite;
using Pulse.Core;
using Pulse.Storage;
using System.Text.Json;
using System.Security.Cryptography;

namespace Pulse.Core.Tests;
public sealed class AmountEditTests
{
    private sealed class Clock:IClock{public DateTimeOffset Now{get;set;}=new(2026,10,6,12,0,0,TimeSpan.FromHours(8));}
    private static readonly DateOnly Day=new(2026,10,6);
    private static ActivityConfig Config=>new("修改专用","book","","count","次",1000,4000,new(),VisualSpec.Default);
    [Fact] public void ConfirmedTotalsRemainExactAndOriginalEventsArePreserved()
    {
        using var store=new SqliteStore(":memory:");var clock=new Clock();var core=new PulseCore(store,clock);var a=core.Create(Guid.NewGuid(),Config);
        core.Add(a.Id,Guid.NewGuid());core.Add(a.Id,Guid.NewGuid());core.Add(a.Id,Guid.NewGuid());
        var original=core.Detail(a.Id,Day).Events.ToArray();
        Assert.Equal(3000,core.Detail(a.Id,Day).Summary.AmountAtoms);
        Assert.Equal("confirmation_required",Assert.Throws<PulseException>(()=>core.SetToday(a.Id,Guid.NewGuid(),Day,385000,false)).Code);
        Assert.Equal(3000,core.Detail(a.Id,Day).Summary.AmountAtoms);
        var id=Guid.NewGuid();var result=core.SetToday(a.Id,id,Day,385000,true);Assert.Equal(385000,result.Summary.AmountAtoms);
        Assert.Equal(result,core.SetToday(a.Id,id,Day,385000,true));
        Assert.Equal("request_conflict",Assert.Throws<PulseException>(()=>core.SetToday(a.Id,id,Day,1,true)).Code);
        Assert.Equal(384000,core.Decrement(a.Id,Guid.NewGuid()).Summary.AmountAtoms);
        Assert.Equal(3000,core.SetToday(a.Id,Guid.NewGuid(),Day,3000,true).Summary.AmountAtoms);
        Assert.All(original,e=>Assert.Equal(e,core.Detail(a.Id,Day).Events.Single(x=>x.Id==e.Id)));
        Assert.Equal(2000,core.Decrement(a.Id,Guid.NewGuid()).Summary.AmountAtoms);
        Assert.Equal(0,core.SetToday(a.Id,Guid.NewGuid(),Day,0,true).Summary.RecordedUnits);
        Assert.Equal(0,core.Growth(a.Id,Day).Value);
        Assert.Equal(9223372036854775,core.SetToday(a.Id,Guid.NewGuid(),Day,9223372036854775000,true).Summary.RecordedUnits);
        Assert.Equal(.05,core.Growth(a.Id,Day).Value,8);
    }
    [Fact] public void MidnightArchiveAndOtherActivitiesAreRespected()
    {
        using var store=new SqliteStore(":memory:");var clock=new Clock();var core=new PulseCore(store,clock);var a=core.Create(Guid.NewGuid(),Config);
        var b=core.Create(Guid.NewGuid(),Config with{Name="保留",Visual=Config.Visual with{AccentColor="#123456"}});core.Add(b.Id,Guid.NewGuid());
        var before=core.Detail(b.Id,Day);core.SetToday(a.Id,Guid.NewGuid(),Day,10000,true);
        Assert.Equal(before.Summary,core.Detail(b.Id,Day).Summary);
        core.Archive(a.Id,true);Assert.Equal("archived",Assert.Throws<PulseException>(()=>core.SetToday(a.Id,Guid.NewGuid(),Day,1,true)).Code);
        core.Archive(a.Id,false);clock.Now=clock.Now.AddDays(1);
        Assert.Equal("date_changed",Assert.Throws<PulseException>(()=>core.SetToday(a.Id,Guid.NewGuid(),Day,1,true)).Code);
        Assert.Equal(10000,core.Detail(a.Id,Day).Summary.AmountAtoms);Assert.Equal(0,core.Detail(a.Id,Day.AddDays(1)).Summary.AmountAtoms);
    }
    [Fact] public void LegacyReceiptsMigrateAndReopenKeepsReplayAndFacts()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"isolated-data",Guid.NewGuid()+".db");var clock=new Clock();Guid id;var request=Guid.NewGuid();
        using(var store=new SqliteStore(path)){var core=new PulseCore(store,clock);id=core.Create(Guid.NewGuid(),Config).Id;core.Add(id,request);}
        using(var db=new SqliteConnection("Data Source="+path))
        {
            db.Open();using var command=db.CreateCommand();command.CommandText="ALTER TABLE command_receipts RENAME TO original_receipts; CREATE TABLE command_receipts(id TEXT PRIMARY KEY NOT NULL,activity_id TEXT NOT NULL REFERENCES activities(id),operation TEXT NOT NULL CHECK(operation IN ('add','decrement')),result_json TEXT NOT NULL CHECK(json_valid(result_json))); INSERT INTO command_receipts SELECT * FROM original_receipts; DROP TABLE original_receipts;";command.ExecuteNonQuery();
        }
        var fingerprint=Fingerprint(path);
        using(var store=new SqliteStore(path)){Assert.Equal(fingerprint,Fingerprint(path));var core=new PulseCore(store,clock);Assert.Equal(1000,core.Add(id,request).Summary.AmountAtoms);core.SetToday(id,Guid.NewGuid(),Day,3000,true);}
        using(var store=new SqliteStore(path)){var core=new PulseCore(store,clock);Assert.Equal(3000,core.Detail(id,Day).Summary.AmountAtoms);Assert.Equal(1000,core.Add(id,request).Summary.AmountAtoms);}
        // Optional local profile copy: the live database is never opened by this test.
        if(Environment.GetEnvironmentVariable("PULSE_PROFILE_COPY") is string copy)
        {
            var saved=Fingerprint(copy);using(var migrated=new SqliteStore(copy))Assert.Equal(saved,Fingerprint(copy));
        }
    }
    private static string Fingerprint(string path)
    {
        using var db=new SqliteConnection("Data Source="+path+";Mode=ReadOnly");db.Open();var tables=new List<object>();
        foreach(var table in new[]{"activities","events","command_receipts"})
        {
            using var command=db.CreateCommand();command.CommandText="SELECT "+(table=="events"?"event_no,id,activity_id,recorded_at_ms,local_date,utc_offset_minutes,delta_steps,delta_amount_atoms,reverses_id":"*")+" FROM "+table+" ORDER BY 1";using var rows=command.ExecuteReader();var records=new List<object[]>();
            while(rows.Read()){var values=new object[rows.FieldCount];rows.GetValues(values);records.Add(values);}tables.Add(records);
        }
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(tables)));
    }
}
