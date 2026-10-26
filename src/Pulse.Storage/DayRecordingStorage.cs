using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pulse.Core;

namespace Pulse.Storage;
public sealed partial class SqliteStore
{
    private static void MigratePortions(SqliteConnection db,SqliteTransaction transaction)
    {
        using var command=db.CreateCommand();command.Transaction=transaction;
        command.CommandText="ALTER TABLE events ADD COLUMN delta_units INTEGER CHECK(delta_units IS NULL OR (delta_steps=1 AND delta_units>0) OR (delta_steps=-1 AND delta_units<0))";
        command.ExecuteNonQuery();
        // Only known total edits are expanded; old individual clicks retain their original portion.
        // No event ID, timestamp, amount, or receipt is rewritten.
        command.CommandText="""
            UPDATE events SET delta_units=delta_amount_atoms/(SELECT step_atoms FROM activities WHERE activities.id=events.activity_id)
            WHERE delta_steps=1 AND delta_amount_atoms%(SELECT step_atoms FROM activities WHERE activities.id=events.activity_id)=0
              AND id IN (SELECT json_extract(result_json,'$.event.id') FROM command_receipts WHERE operation='set_today');
            UPDATE events SET delta_units=-(SELECT COALESCE(original.delta_units,original.delta_steps) FROM events original WHERE original.id=events.reverses_id)
            WHERE reverses_id IS NOT NULL;
            PRAGMA user_version=3;
            """;
        command.ExecuteNonQuery();
    }
}
internal sealed partial class SqliteTransactionView
{
    public DayPartition? GetPartition(Guid activityId,DateOnly day)
    {
        using var cmd=Command("SELECT body FROM day_partitions WHERE activity_id=$id AND local_date=$day",("$id",activityId.ToString()),("$day",Date(day)));
        return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<DayPartition>(json,Rules.Json):null;
    }
    public void SavePartition(Guid activityId,DateOnly day,DayPartition partition)
    {
        using var cmd=Command("INSERT INTO day_partitions(activity_id,local_date,body) VALUES ($id,$day,$body) ON CONFLICT(activity_id,local_date) DO UPDATE SET body=excluded.body",
            ("$id",activityId.ToString()),("$day",Date(day)),("$body",JsonSerializer.Serialize(partition,Rules.Json)));cmd.ExecuteNonQuery();
    }
    public GroupReceipt? GetGroupReceipt(Guid id)
    {
        using var cmd=Command("SELECT activity_id,input_json,result_json FROM group_receipts WHERE id=$id",("$id",id.ToString()));using var row=cmd.ExecuteReader();
        return row.Read()?new(id,Guid.Parse(row.GetString(0)),row.GetString(1),JsonSerializer.Deserialize<DayRecording>(row.GetString(2),Rules.Json)!):null;
    }
    public void InsertGroupReceipt(GroupReceipt receipt)
    {
        using var cmd=Command("INSERT INTO group_receipts(id,activity_id,input_json,result_json) VALUES ($id,$activity,$input,$result)",
            ("$id",receipt.Id.ToString()),("$activity",receipt.ActivityId.ToString()),("$input",receipt.Input),("$result",JsonSerializer.Serialize(receipt.Result,Rules.Json)));cmd.ExecuteNonQuery();
    }
}
