using System.Text.Json;
using Microsoft.Win32;
using Pulse.Core;
using Pulse.Desktop;
using Pulse.Storage;

namespace Pulse.Host;
internal sealed partial class HostRuntime
{
    private bool maintenance;
    private readonly Dictionary<Guid,object> restoreReceipts=new();
    private string SettingsJson()=>JsonSerializer.Serialize(settings,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,WriteIndented=true});
    private object ExportBackup(JsonElement p)
    {
        var path=Path.GetFullPath(p.GetProperty("path").GetString()!);
        BackupArchive.Export(store,SettingsJson(),path);return new{path,created=true};
    }
    private object RestoreBackup(Guid requestId,JsonElement p)
    {
        if(restoreReceipts.TryGetValue(requestId,out var receipt))return receipt;
        Rules.Require(p.TryGetProperty("confirmed",out var confirmed) && confirmed.ValueKind==JsonValueKind.True,"confirmation_required","恢复需要明确确认：当前数据会被替换，恢复前副本将保留。");
        var root=Path.Combine(dataDirectory,"maintenance");
        var prepared=BackupArchive.Prepare(p.GetProperty("path").GetString()!,root);
        try
        {
            var config=JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(prepared.Settings),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
            Rules.Require(config is not null && config.Version==1 && config.Orbs is not null && config.AnimationMode is "normal" or "economy" && config.LastView is "gallery" or "matrix" && config.Orbs.Values.All(v=>v is not null && v.Monitor is not null && double.IsFinite(v.CenterXDip) && double.IsFinite(v.CenterYDip)),"invalid_backup","备份中的本机设置无效，未修改当前数据。");
            Rules.Require(config!.OrbRotationMultipliers is not null && config.OrbRotationMultipliers.Values.All(v=>double.IsFinite(v)&&v>=.85&&v<=1.5),"invalid_backup","备份中的旋转倍率无效，未修改当前数据。");
            // Startup is a machine preference; importing another backup never enables it.
            config=config! with{StartWithWindows=settings.StartWithWindows};
            File.WriteAllText(prepared.Settings,JsonSerializer.Serialize(config,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower}));
            var safety=Path.Combine(dataDirectory,"backups","before-restore-"+Guid.NewGuid()+".pulse-backup");
            BackupArchive.Export(store,SettingsJson(),safety);
            maintenance=true;pipe.Notify(new{method="maintenance.changed",@params=new{active=true,message="正在恢复备份，暂不接受新记录。"}});
            foreach(var item in orbs.Values)item.Orb.Show(false);
            string rollback;
            try
            {
                rollback=BackupArchive.ReplaceFiles(dataDirectory,prepared,()=>store.CloseForRestore(),()=>{store=new(Path.Combine(dataDirectory,"pulse.db"));Core=new(store,clock);});
                settings=config;
            }
            finally
            {
                maintenance=false;
                foreach(var item in orbs.Values)item.Orb.Dispose();orbs.Clear();
                try{RefreshAll();}catch(Exception error){log.Write(error);}
                pipe.Notify(new{method="maintenance.changed",@params=new{active=false,message=""}});Changed(null,Today);
            }
            var result=new{restored=true,before_restore_backup=safety,before_restore_files=rollback};
            restoreReceipts[requestId]=result;return result;
        }
        finally{Directory.Delete(prepared.Directory,true);}
    }
    private static void SetStartup(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled)key.SetValue("Pulse",'"'+Environment.ProcessPath+'"'+" --run",RegistryValueKind.String);
        else key.DeleteValue("Pulse",false);
    }
}
