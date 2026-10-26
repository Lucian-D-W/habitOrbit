using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pulse.Core;

namespace Pulse.Storage;

public sealed record PreparedBackup(string Directory,string Database,string Settings);
public static class BackupArchive
{
    public static void Export(SqliteStore store,string settings,string destination)
    {
        destination=Path.GetFullPath(destination);
        Rules.Require(Path.GetExtension(destination).Equals(".pulse-backup",StringComparison.OrdinalIgnoreCase),"backup_path","请选择.pulse-backup备份文件。");
        var parent=Path.GetDirectoryName(destination)!;Directory.CreateDirectory(parent);
        var stage=Path.Combine(parent,".pulse-backup-"+Guid.NewGuid());Directory.CreateDirectory(stage);
        try
        {
            store.ExportDatabase(Path.Combine(stage,"pulse.db"));
            File.WriteAllText(Path.Combine(stage,"desktop-settings.json"),settings);
            File.WriteAllText(Path.Combine(stage,"manifest.json"),JsonSerializer.Serialize(new{format="pulse-backup",version=1,schema=3,app="0.1.0",created_utc=DateTimeOffset.UtcNow}));
            var archive=stage+".tmp";ZipFile.CreateFromDirectory(stage,archive,CompressionLevel.Optimal,false);
            File.Move(archive,destination,true);
        }
        finally{Directory.Delete(stage,true);if(File.Exists(stage+".tmp"))File.Delete(stage+".tmp");}
    }
    public static PreparedBackup Prepare(string archive,string stagingRoot)
    {
        var stage=Path.Combine(stagingRoot,"restore-"+Guid.NewGuid());Directory.CreateDirectory(stage);
        try
        {
            using var zip=ZipFile.OpenRead(archive);
            var names=new[]{"manifest.json","pulse.db","desktop-settings.json"};
            Rules.Require(zip.Entries.Count==3 && names.All(n=>zip.Entries.Count(e=>e.FullName==n)==1),"invalid_backup","备份内容不是支持的Pulse格式。");
            foreach(var entry in zip.Entries)
            {
                var max=entry.FullName=="pulse.db"?1024L*1024*1024:1024L*1024;
                Rules.Require(entry.Length<=max,"backup_size","备份超出支持大小。");
                using var input=entry.Open();using var output=File.Create(Path.Combine(stage,entry.FullName));
                var buffer=new byte[65536];long count=0;int size;
                while((size=input.Read(buffer))>0){count+=size;Rules.Require(count<=max,"backup_size","备份超出支持大小。");output.Write(buffer,0,size);}
                output.Flush(true);
            }
            using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(stage,"manifest.json")));
            var m=manifest.RootElement;
            Rules.Require(m.GetProperty("format").GetString()=="pulse-backup" && m.GetProperty("version").GetInt32()==1 && m.GetProperty("schema").GetInt32() is 1 or 2 or 3,"backup_version","备份版本不支持，当前数据未改动。");
            var database=Path.Combine(stage,"pulse.db");ValidateDatabase(database);
            return new(stage,database,Path.Combine(stage,"desktop-settings.json"));
        }
        catch{Directory.Delete(stage,true);throw;}
    }
    public static void ValidateDatabase(string path)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();
        using var query=db.CreateCommand();query.CommandText="PRAGMA integrity_check";
        Rules.Require((string?)query.ExecuteScalar()=="ok","invalid_backup","备份数据库完整性检查失败。");
        query.CommandText="PRAGMA user_version";Rules.Require(Convert.ToInt64(query.ExecuteScalar()) is 1 or 2 or 3,"backup_version","备份数据库版本不支持。");
        query.CommandText="PRAGMA foreign_key_check";using(var rows=query.ExecuteReader())Rules.Require(!rows.Read(),"invalid_backup","备份数据库关联检查失败。");
        query.CommandText="SELECT id,name,icon_id,description,kind,unit,step_atoms,group_gap_ms,heatmap_breaks_json,visual_json,visual_key,created_at_ms,created_local_date,updated_at_ms,archived_at_ms FROM activities LIMIT 0";query.ExecuteNonQuery();
        query.CommandText="SELECT event_no,id,activity_id,recorded_at_ms,local_date,utc_offset_minutes,delta_steps,delta_amount_atoms,reverses_id FROM events LIMIT 0";query.ExecuteNonQuery();
    }
    public static string ReplaceFiles(string dataDirectory,PreparedBackup prepared,Action close,Action reopen)
    {
        var rollback=Path.Combine(dataDirectory,"backups","before-restore-"+Guid.NewGuid());Directory.CreateDirectory(rollback);
        var marker=Path.Combine(dataDirectory,"restore-pending.json");var database=Path.Combine(dataDirectory,"pulse.db");var settings=Path.Combine(dataDirectory,"desktop-settings.json");
        close();
        try
        {
            File.WriteAllText(marker,JsonSerializer.Serialize(new{rollback}));
            if(File.Exists(settings))File.Copy(settings,Path.Combine(rollback,"desktop-settings.json"));
            File.Move(database,Path.Combine(rollback,"pulse.db"));
            File.Move(prepared.Database,database);File.Copy(prepared.Settings,settings,true);
            reopen();File.Delete(marker);return rollback;
        }
        catch
        {
            try{close();}catch(ObjectDisposedException){}
            Recover(dataDirectory);reopen();throw;
        }
    }
    public static void Recover(string dataDirectory)
    {
        var marker=Path.Combine(dataDirectory,"restore-pending.json");if(!File.Exists(marker))return;
        using var json=JsonDocument.Parse(File.ReadAllText(marker));
        var rollback=Path.GetFullPath(json.RootElement.GetProperty("rollback").GetString()!);
        var allowed=Path.GetFullPath(Path.Combine(dataDirectory,"backups"))+Path.DirectorySeparatorChar;
        Rules.Require(rollback.StartsWith(allowed,StringComparison.OrdinalIgnoreCase),"invalid_recovery","恢复日志路径无效，未修改数据。");
        var old=Path.Combine(rollback,"pulse.db");var database=Path.Combine(dataDirectory,"pulse.db");
        if(File.Exists(old))
        {
            if(File.Exists(database))File.Move(database,Path.Combine(rollback,"failed-restored.db"),true);
            foreach(var suffix in new[]{"-wal","-shm"})if(File.Exists(database+suffix))File.Move(database+suffix,Path.Combine(rollback,"failed-restored.db"+suffix),true);
            File.Copy(old,database);
            var settings=Path.Combine(dataDirectory,"desktop-settings.json");var saved=Path.Combine(rollback,"desktop-settings.json");
            if(File.Exists(saved))File.Copy(saved,settings,true);else if(File.Exists(settings))File.Move(settings,Path.Combine(rollback,"failed-settings.json"),true);
        }
        File.Delete(marker);
    }
}
