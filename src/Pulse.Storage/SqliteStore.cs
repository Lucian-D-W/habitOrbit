using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pulse.Core;

namespace Pulse.Storage;

public sealed partial class SqliteStore : IStore
{
    private readonly SqliteConnection connection;
    private readonly object gate = new();
    private bool disposed;
    public SqliteStore(string path)
    {
        if(path!=":memory:") { path=Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); }
        connection=new(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,DefaultTimeout=3}.ToString());
        connection.Open();
        try
        {
            var script=Resource("schema.sql"); var ddl=script.IndexOf("CREATE TABLE",StringComparison.Ordinal);
            using var version=connection.CreateCommand(); version.CommandText="PRAGMA user_version";
            var schemaVersion=Convert.ToInt64(version.ExecuteScalar());
            Rules.Require(schemaVersion is 0 or 1 or 2 or 3,"schema_version","数据库结构需由对应版本迁移，当前未修改原库。");
            using(var probe=connection.CreateCommand())
            {
                probe.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                using var rows=probe.ExecuteReader(); var tables=new List<string>(); while(rows.Read())tables.Add(rows.GetString(0));
                Rules.Require(schemaVersion==0?tables.Count==0:tables.Contains("activities")&&tables.Contains("events"),"foreign_database","目标不是Pulse数据库，未修改。");
            }
            using(var setup=connection.CreateCommand()) { setup.CommandText=script[..ddl]; setup.ExecuteNonQuery(); }
            if(schemaVersion==2 && path!=":memory:" && !File.Exists(path+".before-schema-3.db"))
            {
                using var backup=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path+".before-schema-3.db",Pooling=false}.ToString());
                backup.Open();connection.BackupDatabase(backup);
            }
            if(schemaVersion==1)
            {
                if(path!=":memory:" && !File.Exists(path+".before-schema-2.db"))
                {
                    using var backup=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path+".before-schema-2.db",Pooling=false}.ToString());
                    backup.Open();connection.BackupDatabase(backup);
                }
                using var foreignKeys=connection.CreateCommand();foreignKeys.CommandText="PRAGMA foreign_keys=OFF";foreignKeys.ExecuteNonQuery();
            }
            using var transaction=connection.BeginTransaction();
            using var command=connection.CreateCommand(); command.Transaction=transaction;
            if(schemaVersion==0)
            {
                command.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                Rules.Require(Convert.ToInt64(command.ExecuteScalar())==0,"foreign_database","目标不是空的Pulse数据库，未覆盖。");
                command.CommandText=script[ddl..]; command.ExecuteNonQuery();
            }
            if(schemaVersion==1)MigrateMeasurement(connection,transaction,script);
            command.CommandText=Resource("command-receipts.sql"); command.ExecuteNonQuery();
            command.CommandText="SELECT sql FROM sqlite_master WHERE type='table' AND name='command_receipts'";
            var receiptSchema=(string)command.ExecuteScalar()!;
            if(!receiptSchema.Contains("'set_today'",StringComparison.Ordinal))
            {
                // One forward change to technical receipts; business facts and UUID results are copied unchanged.
                command.CommandText="ALTER TABLE command_receipts RENAME TO previous_command_receipts";command.ExecuteNonQuery();
                command.CommandText=Resource("command-receipts.sql");command.ExecuteNonQuery();
                command.CommandText="INSERT INTO command_receipts SELECT * FROM previous_command_receipts; DROP TABLE previous_command_receipts";command.ExecuteNonQuery();
            }
            if(schemaVersion is 1 or 2)MigratePortions(connection,transaction);
            command.CommandText=Resource("day-partitions.sql");command.ExecuteNonQuery();
            command.CommandText="CREATE TABLE IF NOT EXISTS gallery_layout(id INTEGER PRIMARY KEY CHECK(id=1),body TEXT NOT NULL)";
            command.ExecuteNonQuery();
            transaction.Commit();
            using var enableKeys=connection.CreateCommand();enableKeys.CommandText="PRAGMA foreign_keys=ON";enableKeys.ExecuteNonQuery();
        }
        catch { connection.Dispose(); throw; }
    }
    private static string Resource(string name)
    {
        using var stream=typeof(SqliteStore).Assembly.GetManifestResourceStream("Pulse.Storage."+name)!;
        using var reader=new StreamReader(stream); return reader.ReadToEnd();
    }
    public T Transaction<T>(Func<ITransaction,T> action)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            try
            {
                using var transaction=connection.BeginTransaction();
                var result=action(new SqliteTransactionView(connection,transaction));transaction.Commit();return result;
            }
            catch(SqliteException error)
            {
                if(error.Message.Contains("activities.visual_key",StringComparison.Ordinal)) throw new PulseException("visual_duplicate","已有星球使用完全相同的可见外观参数。");
                if(error.SqliteErrorCode is 5 or 6) throw new PulseException("storage_busy","数据库忙，请稍后重试此请求。");
                if(error.Message.Contains("integer overflow",StringComparison.Ordinal)) throw new PulseException("numeric_limit","累积数量超出支持范围，本次未记录。");
                throw new PulseException("storage_failed","数据库操作未完成，本次事务已回滚。");
            }
        }
    }
    public void ExportDatabase(string path)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            using var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            target.Open();connection.BackupDatabase(target);
        }
    }
    public void CloseForRestore()
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            using var checkpoint=connection.CreateCommand();checkpoint.CommandText="PRAGMA wal_checkpoint(TRUNCATE)";
            using(var row=checkpoint.ExecuteReader())if(row.Read() && row.GetInt64(0)!=0)throw new IOException("数据库仍被其他连接占用，恢复未开始。");
            disposed=true;connection.Dispose();
        }
    }
    public void Dispose() { lock(gate) { if(disposed)return; disposed=true; connection.Dispose(); } }
}

internal sealed partial class SqliteTransactionView(SqliteConnection connection, SqliteTransaction transaction) : ITransaction
{
    private const string ActivityColumns="id,name,icon_id,description,kind,unit,step_atoms,group_gap_ms,heatmap_breaks_json,visual_json,visual_key,created_at_ms,created_local_date,updated_at_ms,archived_at_ms";
    private const string EventColumns="event_no,id,activity_id,recorded_at_ms,local_date,utc_offset_minutes,delta_steps,delta_amount_atoms,reverses_id,COALESCE(delta_units,delta_steps)";
    private SqliteCommand Command(string sql, params (string,object?)[] parameters)
    {
        var cmd=connection.CreateCommand(); cmd.Transaction=transaction; cmd.CommandText=sql;
        foreach(var (name,value) in parameters) cmd.Parameters.AddWithValue(name,value??DBNull.Value);
        return cmd;
    }
    private static string Date(DateOnly day)=>day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    private static DateOnly Date(string day)=>DateOnly.ParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture);
    private static Activity ReadActivity(SqliteDataReader row)
    {
        var breaks=JsonSerializer.Deserialize<long[]>(row.GetString(8))!;
        return new(Guid.Parse(row.GetString(0)),new(row.GetString(1),row.GetString(2),row.GetString(3),row.GetString(4),row.GetString(5),
            row.GetInt64(6),row.IsDBNull(7)?null:row.GetInt64(7),new(breaks[0],breaks[1],breaks[2]),JsonSerializer.Deserialize<VisualSpec>(row.GetString(9),Rules.Json)!),
            row.GetString(10),row.GetInt64(11),Date(row.GetString(12)),row.GetInt64(13),row.IsDBNull(14)?null:row.GetInt64(14));
    }
    private static RecordEvent ReadEvent(SqliteDataReader row)=>new(row.GetInt64(0),Guid.Parse(row.GetString(1)),Guid.Parse(row.GetString(2)),row.GetInt64(3),
        Date(row.GetString(4)),row.GetInt32(5),row.GetInt32(6),row.GetInt64(7),row.IsDBNull(8)?null:Guid.Parse(row.GetString(8))){DeltaUnits=row.GetInt64(9)};
    public Activity? GetActivity(Guid id)
    {
        using var cmd=Command($"SELECT {ActivityColumns} FROM activities WHERE id=$id",("$id",id.ToString()));
        using var row=cmd.ExecuteReader(); return row.Read()?ReadActivity(row):null;
    }
    public IReadOnlyList<Activity> ListActivities(bool includeArchived)
    {
        using var cmd=Command($"SELECT {ActivityColumns} FROM activities WHERE $all OR archived_at_ms IS NULL ORDER BY created_at_ms,id",("$all",includeArchived));
        using var row=cmd.ExecuteReader(); var result=new List<Activity>(); while(row.Read())result.Add(ReadActivity(row)); return result;
    }
    public void DeleteActivity(Guid id)
    {
        using var command=Command("DELETE FROM day_partitions WHERE activity_id=$id; DELETE FROM group_receipts WHERE activity_id=$id; DELETE FROM command_receipts WHERE activity_id=$id; DELETE FROM events WHERE activity_id=$id AND reverses_id IS NOT NULL; DELETE FROM events WHERE activity_id=$id; DELETE FROM activities WHERE id=$id",("$id",id.ToString()));
        command.ExecuteNonQuery();
    }
    public bool HasEvents(Guid activityId)
    {
        using var cmd=Command("SELECT EXISTS(SELECT 1 FROM events WHERE activity_id=$id)",("$id",activityId.ToString()));
        return Convert.ToInt64(cmd.ExecuteScalar())!=0;
    }
    private SqliteCommand ActivityCommand(Activity a, string sql)=>Command(sql,
        ("$id",a.Id.ToString()),("$name",a.Config.Name),("$icon",a.Config.IconId),("$description",a.Config.Description),
        ("$kind",a.Config.Kind),("$unit",a.Config.Unit),("$step",a.Config.StepAtoms),("$gap",a.Config.GroupGapMs),
        ("$breaks",JsonSerializer.Serialize(new[]{a.Config.Breaks.B1,a.Config.Breaks.B2,a.Config.Breaks.B3})),
        ("$visual",JsonSerializer.Serialize(a.Config.Visual,Rules.Json)),("$key",a.VisualKey),("$created",a.CreatedAtMs),
        ("$date",Date(a.CreatedLocalDate)),("$updated",a.UpdatedAtMs),("$archived",a.ArchivedAtMs));
    public void InsertActivity(Activity activity)
    {
        using var cmd=ActivityCommand(activity,$"INSERT INTO activities ({ActivityColumns}) VALUES ($id,$name,$icon,$description,$kind,$unit,$step,$gap,$breaks,$visual,$key,$created,$date,$updated,$archived)");
        cmd.ExecuteNonQuery();
    }
    public void UpdateActivity(Activity activity)
    {
        using var cmd=ActivityCommand(activity,"UPDATE activities SET name=$name,icon_id=$icon,description=$description,kind=$kind,unit=$unit,step_atoms=$step,group_gap_ms=$gap,heatmap_breaks_json=$breaks,visual_json=$visual,visual_key=$key,updated_at_ms=$updated,archived_at_ms=$archived WHERE id=$id");
        cmd.ExecuteNonQuery();
    }
    public CommandReceipt? GetReceipt(Guid id)
    {
        using var cmd=Command("SELECT activity_id,operation,result_json FROM command_receipts WHERE id=$id",("$id",id.ToString()));
        using var row=cmd.ExecuteReader();
        return row.Read()?new(id,Guid.Parse(row.GetString(0)),row.GetString(1),JsonSerializer.Deserialize<CountResult>(row.GetString(2),Rules.Json)!):null;
    }
    public void InsertReceipt(CommandReceipt receipt)
    {
        using var cmd=Command("INSERT INTO command_receipts(id,activity_id,operation,result_json) VALUES ($id,$activity,$operation,$result)",
            ("$id",receipt.Id.ToString()),("$activity",receipt.ActivityId.ToString()),("$operation",receipt.Operation),("$result",JsonSerializer.Serialize(receipt.Result,Rules.Json)));
        cmd.ExecuteNonQuery();
    }
    public RecordEvent? LatestUnreversed(Guid activityId, DateOnly day)
    {
        using var cmd=Command($"SELECT {EventColumns} FROM events e WHERE activity_id=$id AND local_date=$day AND delta_steps=1 AND NOT EXISTS(SELECT 1 FROM events r WHERE r.reverses_id=e.id) ORDER BY event_no DESC LIMIT 1",("$id",activityId.ToString()),("$day",Date(day)));
        using var row=cmd.ExecuteReader(); return row.Read()?ReadEvent(row):null;
    }
    public RecordEvent InsertEvent(RecordEvent record)
    {
        using var cmd=Command("INSERT INTO events(id,activity_id,recorded_at_ms,local_date,utc_offset_minutes,delta_steps,delta_amount_atoms,reverses_id,delta_units) VALUES ($id,$activity,$time,$day,$offset,$steps,$amount,$reverses,$units) RETURNING event_no",
            ("$id",record.Id.ToString()),("$activity",record.ActivityId.ToString()),("$time",record.RecordedAtMs),("$day",Date(record.LocalDate)),
            ("$offset",record.UtcOffsetMinutes),("$steps",record.DeltaSteps),("$amount",record.DeltaAmountAtoms),("$reverses",record.ReversesId?.ToString()),("$units",record.DeltaUnits));
        return record with {EventNo=Convert.ToInt64(cmd.ExecuteScalar())};
    }
    public IReadOnlyList<RecordEvent> DayEvents(Guid activityId, DateOnly day)
    {
        using var cmd=Command($"SELECT {EventColumns} FROM events WHERE activity_id=$id AND local_date=$day ORDER BY event_no",("$id",activityId.ToString()),("$day",Date(day)));
        using var row=cmd.ExecuteReader(); var result=new List<RecordEvent>(); while(row.Read())result.Add(ReadEvent(row)); return result;
    }
    public IReadOnlyList<DayAggregate> DailyAggregates(Guid id, DateOnly from, DateOnly to)=>Aggregates("activity_id=$id AND",from,to,("$id",id.ToString()));
    public IReadOnlyList<DayAggregate> MatrixAggregates(DateOnly from, DateOnly to)=>Aggregates("",from,to);
    private IReadOnlyList<DayAggregate> Aggregates(string filter, DateOnly from, DateOnly to, params (string,object?)[] extra)
    {
        using var cmd=Command($"SELECT activity_id,local_date,SUM(delta_amount_atoms),SUM(COALESCE(delta_units,delta_steps)) FROM events WHERE {filter} local_date BETWEEN $from AND $to GROUP BY activity_id,local_date ORDER BY local_date",extra.Concat(new (string,object?)[]{("$from",Date(from)),("$to",Date(to))}).ToArray());
        using var row=cmd.ExecuteReader(); var result=new List<DayAggregate>();
        while(row.Read())result.Add(new(Guid.Parse(row.GetString(0)),Date(row.GetString(1)),row.GetInt64(2),row.GetInt64(3))); return result;
    }
}
