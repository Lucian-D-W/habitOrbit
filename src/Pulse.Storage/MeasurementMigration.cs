using Microsoft.Data.Sqlite;
using Pulse.Core;

namespace Pulse.Storage;
public sealed partial class SqliteStore
{
    // SQLite's generalized ALTER TABLE procedure, inside the caller's transaction.
    // The logical rows and references remain unchanged; only the duration CHECK is relaxed.
    private static void MigrateMeasurement(SqliteConnection db,SqliteTransaction transaction,string schema)
    {
        using var command=db.CreateCommand();command.Transaction=transaction;
        command.CommandText="SELECT sql FROM sqlite_master WHERE tbl_name='activities' AND type IN ('index','trigger') AND sql IS NOT NULL";
        var objects=new List<string>();using(var rows=command.ExecuteReader())while(rows.Read())objects.Add(rows.GetString(0));
        var start=schema.IndexOf("CREATE TABLE activities",StringComparison.Ordinal);
        var end=schema.IndexOf("CREATE TABLE events",StringComparison.Ordinal);
        command.CommandText=schema[start..end].Replace("CREATE TABLE activities","CREATE TABLE migrated_activities",StringComparison.Ordinal);
        command.ExecuteNonQuery();
        command.CommandText="INSERT INTO migrated_activities SELECT * FROM activities; DROP TABLE activities; ALTER TABLE migrated_activities RENAME TO activities";
        command.ExecuteNonQuery();
        foreach(var sql in objects){command.CommandText=sql;command.ExecuteNonQuery();}
        command.CommandText="PRAGMA foreign_key_check";
        using(var rows=command.ExecuteReader())Rules.Require(!rows.Read(),"migration_failed","数据关联检查失败，迁移已回滚。");
        command.CommandText="PRAGMA user_version=2";command.ExecuteNonQuery();
    }
}
