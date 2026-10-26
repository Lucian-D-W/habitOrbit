using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Pulse.Core;
using Pulse.Desktop;
using Pulse.Storage;
using Activity=Pulse.Core.Activity;

namespace Pulse.Host;

internal sealed partial class HostRuntime:IDisposable
{
    private SqliteStore store;
    private readonly string dataDirectory;
    internal PulseCore Core;
    private readonly IClock clock;
    private readonly SettingsFile settingsFile;
    private DesktopSettings settings;
    private readonly CanvasDevice device;
    private readonly Dictionary<Guid,(Activity Activity,DayDetail Day,DesktopOrb Orb,float Maturity)> orbs=new();
    private readonly ConcurrentQueue<Action> commands=new();
    private readonly RunLog log;
    private readonly bool hidden;
    private readonly nint dispatcher;
    private readonly PipeServer pipe;
    private readonly NativeTray? tray;
    private readonly Mutex singleton;
    private bool temporaryHidden,disposed;
    private DateOnly currentDate;
    private long revision;
    private Pulse.Rendering.OrbRenderer? previewRenderer;
    private string? previewKey,previewSession;
    private readonly List<double> feedbackMs=new();
    public string PipeName {get;}
    public static string UserKey => WindowsIdentity.GetCurrent().User?.Value??throw new InvalidOperationException("没有当前用户SID。");
    public HostRuntime(string directory,IClock? time=null,bool hidden=false)
    {
        directory=Path.GetFullPath(directory);dataDirectory=directory;this.hidden=hidden;clock=time??new SystemClock();
        if(ProcessEnvironment.IsPackaged())throw new InvalidOperationException("Pulse当前要求非MSIX部署。");
        var suffix=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(UserKey+"|"+directory.ToUpperInvariant())))[..20];
        PipeName="Pulse.v1."+suffix;
        singleton=new Mutex(true,"Global\\"+PipeName,out var created);
        if(!created){singleton.Dispose();throw new PulseException("already_running","当前用户已有此数据目录的 Pulse Host。");}
        device=new();
        Directory.CreateDirectory(directory);log=new(Path.Combine(directory,"logs"));
        BackupArchive.Recover(directory);
        settingsFile=new(Path.Combine(directory,"desktop-settings.json"),log.Write);settings=settingsFile.Load();
        store=new(Path.Combine(directory,"pulse.db"));Core=new(store,clock);currentDate=Today;
        dispatcher=NativeWindow.Create("Pulse Host dispatcher",0,NativeWindow.ToolWindow,0,0,1,1,Message);
        pipe=new(PipeName,line=>Dispatch(()=>Handle(line)),log.Write,()=>PostMessageW(dispatcher,0x10,0,0));
        RefreshAll();
        if(!hidden)tray=new NativeTray(dispatcher,OpenManager,ToggleTemporary,RequestApplicationExit);
        if(NativeWindow.SetTimer(dispatcher,3,1000,0)==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        pipe.Start();
    }
    private DateOnly Today => DateOnly.FromDateTime(clock.Now.DateTime);
    private Task<T> Dispatch<T>(Func<T> action)
    {
        var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(disposed){result.SetException(new ObjectDisposedException(nameof(HostRuntime)));return result.Task;}
        commands.Enqueue(()=>{try{result.SetResult(action());}catch(Exception e){result.SetException(e);}});
        if(!PostMessageW(dispatcher,0x8001,0,0))result.TrySetException(new IOException("Host调度器不可用。"));
        return result.Task;
    }
    private nint? Message(uint message,nuint wp,nint lp)
    {
        if(tray?.Message(message,wp,lp)==true)return 0;
        if(message==0x8001){for(var i=0;i<32 && commands.TryDequeue(out var action);i++)action();if(!commands.IsEmpty)PostMessageW(dispatcher,0x8001,0,0);return 0;}
        if(message==0x8002){currentDate=Today;RefreshAll();return 0;}
        if(message==0x113 && wp==1){foreach(var item in orbs.Values)if(item.Orb.Visible && !item.Orb.Locked)item.Orb.RenderFrame();return 0;}
        if(message==0x113 && wp==3){Tick();return 0;}
        if(message==0x7e){RefreshAll();return 0;}
        if(message==0x10){NativeWindow.PostQuitMessage(0);return 0;}
        return null;
    }
    internal void Tick()
    {
        if(Today!=currentDate){currentDate=Today;RefreshAll();Changed(null,currentDate);}
        foreach(var item in orbs.Values)
            if(item.Activity.Config.GroupGapMs is long && item.Orb.Visible)item.Orb.ApplyState(OrbProjection.State(item.Activity,item.Day,clock.Now,item.Maturity),false);
        SetAnimation();
    }
    private void SetAnimation()
    {
        if(orbs.Values.All(x=>!x.Orb.Visible || x.Orb.Locked)){NativeWindow.KillTimer(dispatcher,1);return;}
        var saver=NativeWindow.GetSystemPowerStatus(out var power) && (power.Saver!=0 || power.ACLine==0);
        if(NativeWindow.SetTimer(dispatcher,1,settings.AnimationMode=="economy" || saver?67u:33u,0)==0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    private void RefreshAll()
    {
        var activities=Core.List();var ids=activities.Select(x=>x.Id).ToHashSet();
        foreach(var id in orbs.Keys.Where(id=>!ids.Contains(id)).ToArray()){orbs[id].Orb.Dispose();orbs.Remove(id);}
        foreach(var activity in activities)Refresh(activity.Id);
        SetAnimation();
    }
    private void Refresh(Guid id)
    {
        var activity=Core.Activity(id);
        if(activity.ArchivedAtMs is not null){if(orbs.Remove(id,out var old))old.Orb.Dispose();SetAnimation();return;}
        var day=Core.Detail(id,Today);
        if(!settings.OrbRotationMultipliers.TryGetValue(id,out var multiplier))
        {
            multiplier=.85+Random.Shared.NextDouble()*.65;
            settings.OrbRotationMultipliers.Add(id,multiplier);
            try{SaveSettings();}catch{settings.OrbRotationMultipliers.Remove(id);throw;}
        }
        var identity=OrbProjection.Identity(activity) with {RotationMultiplier=multiplier};
        DesktopOrb orb;
        if(orbs.TryGetValue(id,out var prior))
        {
            orb=prior.Orb;
            if(prior.Activity.VisualKey!=activity.VisualKey || prior.Activity.Config.Name!=activity.Config.Name ||
                prior.Activity.Config.Visual.DecorativeSeed!=activity.Config.Visual.DecorativeSeed ||
                orb.Identity.RotationMultiplier!=multiplier)orb.SetIdentity(identity);
        }
        else
        {
            orb=new(identity,device,true,56);
            orb.CountRequested=add=>Count(id,Guid.NewGuid(),add,true);
            orb.PositionSaved=point=>SavePosition(id,point);
            orb.EnvironmentChanged=()=>PostMessageW(dispatcher,0x8002,0,0);
            var displays=DesktopLayout.Displays();
            if(settings.Orbs.TryGetValue(id,out var placement))
            {
                var monitor=displays.FirstOrDefault(x=>x.Id==placement.Monitor)??displays[0];
                try{orb.Place(monitor.Restore(placement));}catch(OverflowException){orb.Place(DesktopLayout.FreePosition(displays,orbs.Values.Select(x=>x.Orb.Center).ToArray()));}
            }
            else
            {
                orb.Place(DesktopLayout.FreePosition(displays,orbs.Values.Select(x=>x.Orb.Center).ToArray()));
                settings.Orbs[id]=DesktopLayout.Nearest(displays,orb.Center).Save(orb.Center,false);SaveSettings();
            }
        }
        var maturity=(float)Core.Growth(id,Today).Value;
        orb.ApplyState(OrbProjection.State(activity,day,clock.Now,maturity));orbs[id]=(activity,day,orb,maturity);
        orb.Show(!hidden && !temporaryHidden && settings.Orbs[id].Visible);
    }
    private void SaveSettings(){try{settingsFile.Save(settings);}catch(Exception e){log.Write(e);throw new PulseException("settings_failed","本机设置保存失败，数据库记录不受影响。");}}
    private void SavePosition(Guid id,ScreenPoint point)
    {
        try{settings.Orbs[id]=DesktopLayout.Nearest(DesktopLayout.Displays(),point).Save(point,settings.Orbs[id].Visible);SaveSettings();}
        catch(Exception e){log.Write(e);orbs[id].Orb.ShowError("位置保存失败，请检查磁盘。");}
    }
    internal CountResult? Count(Guid id,Guid eventId,bool add,bool fromDesktop=false)
    {
        var start=Stopwatch.GetTimestamp();
        CountResult result;
        try
        {
            Rules.Require(!maintenance,"maintenance","正在恢复备份，暂不接受新记录。");
            result=add?Core.Add(id,eventId):Core.Decrement(id,eventId);
        }
        catch(Exception e)
        {
            log.Write(e);if(!fromDesktop)throw;
            if(orbs.TryGetValue(id,out var item))item.Orb.ShowError("未保存："+(e is PulseException?e.Message:"请检查磁盘与日志"));
            return null;
        }
        // A presentation failure after commit cannot be reported as a failed write.
        try{Refresh(id);if(!fromDesktop && orbs.TryGetValue(id,out var item) && item.Orb.Visible)item.Orb.RenderFrame();}
        catch(Exception e){log.Write(e);if(orbs.TryGetValue(id,out var item))item.Orb.ShowError("记录已保存，显示刷新失败。");}
        if(result.Status=="changed")Changed(id,result.Summary.Date);
        feedbackMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);if(feedbackMs.Count>1000)feedbackMs.RemoveAt(0);
        return result;
    }
    private void Changed(Guid? id,DateOnly date) => pipe.Notify(new {method="state.changed",@params=new {activityId=id,date,revision=++revision}});
    private object Snapshot()
    {
        Tick();
        return new {date=Today,revision,activities=Core.List(true).Select(ActivityRow).ToArray(),settings=new {settings.AnimationMode,settings.StartWithWindows,settings.LastView},temporaryHidden};
    }
    internal object Handle(string line)
    {
        string? id=null;
        try
        {
            using var doc=JsonDocument.Parse(line);var root=doc.RootElement;id=root.GetProperty("id").GetString();
            if(!Guid.TryParse(id,out var requestId) || requestId==Guid.Empty)throw new PulseException("invalid_id","请求UUID无效。");
            var method=root.GetProperty("method").GetString();var p=root.TryGetProperty("params",out var input)?input:default;
            object? result=method switch
            {
                "state.snapshot" or "state.subscribe"=>Snapshot(),
                "gallery.get"=>Gallery(),"gallery.save"=>SaveGallery(p),
                "state.activity"=>ActivityState(p),
                "activity.list"=>Core.List(p.ValueKind==JsonValueKind.Object && p.TryGetProperty("includeArchived",out var archived) && archived.GetBoolean()),
                "activity.get"=>Core.Activity(RpcJson.Id(p)),
                "activity.create"=>Create(requestId,p),
                "activity.update"=>Update(p),
                "activity.archive"=>Archive(p,true),"activity.restore"=>Archive(p,false),
                "activity.delete"=>DeleteActivity(p),
                "count.add"=>Count(RpcJson.Id(p),requestId,true),"count.decrement"=>Count(RpcJson.Id(p),requestId,false),
                "count.set_today"=>SetToday(requestId,p),
                "recording.get"=>Core.Recording(RpcJson.Id(p),RpcJson.Day(p,"date")),
                "recording.set_groups"=>SetGroups(requestId,p),
                "desktop.set_visible"=>SetVisible(RpcJson.Id(p),p.GetProperty("visible").GetBoolean()),
                "stats.daily"=>Core.Daily(RpcJson.Id(p),RpcJson.Day(p,"from"),RpcJson.Day(p,"to")),
                "stats.context"=>Core.StatisticsContext(RpcJson.Id(p),p.GetProperty("background").GetString()??"#171B29"),
                "stats.view"=>StatisticsView(p),
                "stats.matrix_view"=>MatrixView(p),
                "stats.day_detail"=>DetailPage(p),
                "stats.recent_matrix"=>Core.RecentMatrix(RpcJson.Day(p,"end"),p.GetProperty("days").GetInt32()),
                "preview.render"=>Preview(p),"thumbnail.get"=>Thumbnail(p),
                "preview.release"=>ReleasePreview(p),
                "rules.describe"=>DescribeRules(p),
                "settings.update"=>UpdateSettings(p),
                "backup.export"=>ExportBackup(p),"backup.restore"=>RestoreBackup(requestId,p),
                "environment.info"=>new{data_directory=dataDirectory,physical_data_directory=ProcessEnvironment.PhysicalDirectory(dataDirectory),packaged=ProcessEnvironment.IsPackaged()},
                "environment.fonts"=>Microsoft.Graphics.Canvas.Text.CanvasTextFormat.GetSystemFontFamilies(new[]{"zh-CN","en-US"}).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase).ToArray(),
                "host.metrics"=>Metrics,
                "host.exit"=>Exit(),
                "host.exit_reply"=>pipe.VoteExit(RpcJson.Id(p,"requestId"),RpcJson.Id(p,"managerSession"),p.GetProperty("allow").GetBoolean()),
                _=>throw new PulseException("unknown_method","未知接口。")
            };
            return new {id,ok=true,result};
        }
        catch(Exception e)
        {
            log.Write(e);var code=e is PulseException domain?domain.Code:"invalid_request";
            return new {id,ok=false,error=new {code,message=e is PulseException?e.Message:"请求格式无效或操作失败，请检查日志。"}};
        }
    }
    private ActivityConfig Config(JsonElement p) => p.GetProperty("config").Deserialize<ActivityConfig>(RpcJson.Options)??throw new PulseException("invalid_config","配置为空。");
    private CountResult SetToday(Guid requestId,JsonElement p)
    {
        Rules.Require(!maintenance,"maintenance","正在恢复备份，暂不接受数量修改。");
        var id=RpcJson.Id(p);var result=Core.SetToday(id,requestId,RpcJson.Day(p,"date"),Rules.ParseAtoms(p.GetProperty("amount").GetString()!),p.TryGetProperty("confirmed",out var c)&&c.ValueKind==JsonValueKind.True,(p.TryGetProperty("expectedVersion",out var version)||p.TryGetProperty("expected_version",out version))?version.GetString():null);
        try{Refresh(id);}catch(Exception e){log.Write(e);}
        if(result.Status=="changed")Changed(id,result.Summary.Date);return result;
    }
    private object DetailPage(JsonElement p)
    {
        var offset=p.TryGetProperty("offset",out var o)?o.GetInt32():0;
        var limit=p.TryGetProperty("limit",out var l)?l.GetInt32():500;
        Rules.Require(offset>=0 && limit is >=1 and <=500,"invalid_range","事件页范围无效。");
        var detail=Core.Detail(RpcJson.Id(p),RpcJson.Day(p,"date"));
        return new {detail.Summary,detail.Groups,events=detail.Events.Skip(offset).Take(limit).ToArray(),event_count=detail.Events.Count,offset,
            has_more=(long)offset+limit<detail.Events.Count};
    }
    private object MatrixView(JsonElement p)
    {
        var matrix=Core.RecentMatrix(RpcJson.Day(p,"end"),p.GetProperty("days").GetInt32());
        var background=p.GetProperty("background").GetString()??"#171B29";
        return Core.List().Select(a=>new {id=a.Id,days=matrix[a.Id],colors=Rules.HeatmapColors(background,a.Config.Visual.AccentColor)}).ToArray();
    }
    private object ActivityState(JsonElement p)
    {
        var a=Core.Activity(RpcJson.Id(p));
        return new {date=Today,revision,row=ActivityRow(a)};
    }
    private object ActivityRow(Activity a)
    {
        var detail=Core.Detail(a.Id,Today);
        return new {activity=a,today=detail.Summary,today_groups=detail.Groups,today_version=detail.Recording?.Version,
            desktop=settings.Orbs.GetValueOrDefault(a.Id),has_history=store.Transaction(tx=>tx.HasEvents(a.Id))};
    }
    private object StatisticsView(JsonElement p)
    {
        var id=RpcJson.Id(p);var year=p.GetProperty("year").GetInt32();var days=p.GetProperty("days").GetInt32();
        Rules.Require(year is >=1 and <=9999 && days is 7 or 30 or 90,"invalid_range","统计范围无效。");
        return new {context=Core.StatisticsContext(id,p.GetProperty("background").GetString()??"#171B29"),
            annual=Core.Daily(id,new(year,1,1),new(year,12,31)),recent=Core.Daily(id,Today.AddDays(1-days),Today),revision};
    }
    private Activity Create(Guid requestId,JsonElement p)
    {
        var activity=Core.Create(requestId,Config(p));
        try{Refresh(activity.Id);}catch(Exception e){log.Write(e);}
        Changed(activity.Id,Today);return activity;
    }
    private Activity Update(JsonElement p){var a=Core.Update(RpcJson.Id(p),Config(p));try{Refresh(a.Id);}catch(Exception e){log.Write(e);}Changed(a.Id,Today);return a;}
    private Activity Archive(JsonElement p,bool value){var a=Core.Archive(RpcJson.Id(p),value);try{Refresh(a.Id);}catch(Exception e){log.Write(e);}Changed(a.Id,Today);return a;}
    private object DeleteActivity(JsonElement p)
    {
        var id=RpcJson.Id(p);Core.Delete(id,p.TryGetProperty("confirmed",out var confirmed)&&confirmed.ValueKind==JsonValueKind.True);
        if(orbs.Remove(id,out var old))old.Orb.Dispose();
        if(settings.Orbs.Remove(id)){try{SaveSettings();}catch(Exception e){log.Write(e);}}
        SetAnimation();Changed(null,Today);return new{deleted=true};
    }
    private object SetVisible(Guid id,bool value)
    {
        var a=Core.Activity(id);if(a.ArchivedAtMs is not null)throw new PulseException("archived","归档星球不能显示。");
        if(!orbs.ContainsKey(id))Refresh(id);
        var old=settings.Orbs[id];settings.Orbs[id]=old with {Visible=value};
        try{SaveSettings();}catch{settings.Orbs[id]=old;throw;}
        orbs[id].Orb.Show(value && !temporaryHidden && !hidden);SetAnimation();Changed(id,Today);return settings.Orbs[id];
    }
    private object UpdateSettings(JsonElement p)
    {
        var previous=settings;
        var mode=p.TryGetProperty("animationMode",out var m)?m.GetString():settings.AnimationMode;
        if(mode is not ("normal" or "economy"))throw new PulseException("invalid_animation","动画模式无效。");
        var view=p.TryGetProperty("lastView",out var v)?v.GetString():settings.LastView;
        if(view is not ("gallery" or "matrix"))throw new PulseException("invalid_view","视图无效。");
        var startup=p.TryGetProperty("startWithWindows",out var s)?s.GetBoolean():settings.StartWithWindows;
        settings=settings with {AnimationMode=mode,LastView=view,StartWithWindows=startup};
        try{if(startup!=previous.StartWithWindows)SetStartup(startup);SaveSettings();}catch{settings=previous;SetStartup(previous.StartWithWindows);throw;}
        SetAnimation();Changed(null,Today);return new {settings.AnimationMode,settings.LastView,settings.StartWithWindows};
    }
    internal void ToggleTemporary(){temporaryHidden=!temporaryHidden;foreach(var item in orbs) item.Value.Orb.Show(!temporaryHidden && !hidden && settings.Orbs[item.Key].Visible);SetAnimation();Changed(null,Today);}
    private object Exit()=>pipe.RequestExit();
    private void RequestApplicationExit()
    {
        if(maintenance){tray?.Balloon("暂时不能退出","正在恢复备份，请完成后再退出。");return;}
        var result=JsonSerializer.SerializeToElement(Exit(),RpcJson.Options);
        if(result.GetProperty("exiting").GetBoolean())_ = pipe.FinishExit();
    }
    private object Preview(JsonElement p)
    {
        var visual=p.GetProperty("visual").Deserialize<VisualSpec>(RpcJson.Options)??throw new PulseException("invalid_visual","外观为空。");
        var config=Rules.Normalize(new("预览","circle","","count","次",1000,null,new(),visual));
        var a=new Activity(Guid.Empty,config,Rules.VisualKey(config.Visual),0,Today,0,null);
        if(!p.TryGetProperty("session",out var session))return RenderThumbnail(a,false);
        var energy=p.GetProperty("energy").GetSingle();var time=p.GetProperty("time").GetDouble();
        Rules.Require(float.IsFinite(energy)&&energy>=0&&energy<=1&&double.IsFinite(time)&&time>=0&&time<=86400,"invalid_preview","预览参数无效。");
        var key=a.VisualKey+"|"+config.Visual.DecorativeSeed;
        if(previewRenderer is null||previewKey!=key){previewRenderer?.Dispose();previewRenderer=new(OrbProjection.Identity(a),device);previewKey=key;}
        previewSession=session.GetString();
        return new{png=Convert.ToBase64String(previewRenderer.ThumbnailBytes(energy,time).GetAwaiter().GetResult())};
    }
    private object ReleasePreview(JsonElement p)
    {
        if(p.GetProperty("session").GetString()==previewSession){previewRenderer?.Dispose();previewRenderer=null;previewKey=previewSession=null;}
        return new{released=true};
    }
    private object Thumbnail(JsonElement p)=>RenderThumbnail(Core.Activity(RpcJson.Id(p)),true);
    private object DescribeRules(JsonElement p)
    {
        var config=Rules.Normalize(Config(p));
        return new {colors=Rules.HeatmapColors("#171B29",config.Visual.AccentColor),samples=new long[]{0,config.Breaks.B1-1,config.Breaks.B1,config.Breaks.B2,config.Breaks.B3}
            .Select(amount=>new {amount_atoms=amount,level=Rules.Level(amount,config.Breaks)}).ToArray()};
    }
    private object RenderThumbnail(Activity activity,bool cache)
    {
        var key=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{activity.VisualKey,activity.Config.Visual.DecorativeSeed,activity.Config.Visual.RendererVersion,renderRevision=5},Rules.Json))).ToLowerInvariant();
        var directory=Path.Combine(dataDirectory,"thumbnails");
        var path=Path.Combine(directory,key+".png");
        try
        {
            if(!cache)
            {
                using var preview=new Pulse.Rendering.OrbRenderer(OrbProjection.Identity(activity),device);
                return new{png=Convert.ToBase64String(preview.ThumbnailBytes().GetAwaiter().GetResult()),cache_key=key,visual_key=activity.VisualKey,renderer_version=activity.Config.Visual.RendererVersion};
            }
            Directory.CreateDirectory(directory);
            var valid=false;
            if(cache && File.Exists(path))
            {
                try{using var png=Microsoft.Graphics.Canvas.CanvasBitmap.LoadAsync(device,path).AsTask().GetAwaiter().GetResult();valid=png.SizeInPixels.Width==256 && png.SizeInPixels.Height==256;}
                catch(Exception e){log.Write(e);}
            }
            if(!valid)
            {
                using var renderer=new Pulse.Rendering.OrbRenderer(OrbProjection.Identity(activity),device);
                var temporary=path+".tmp";
                renderer.ExportThumbnail(temporary).GetAwaiter().GetResult();File.Move(temporary,path,true);
            }
            var bytes=File.ReadAllBytes(path);
            if(bytes.Length>1024*1024)throw new IOException("缩略图超出传输上限。");
            return new{png=Convert.ToBase64String(bytes),cache_key=key,visual_key=activity.VisualKey,renderer_version=activity.Config.Visual.RendererVersion};
        }
        catch(Exception e){log.Write(e);throw new PulseException(cache?"thumbnail_failed":"preview_failed",cache?"缩略图暂不可用，星球记录仍保留，请重试。":"预览暂不可用，请重试。");}
    }
    internal Task<T> CheckOnThread<T>(Func<T> action)=>Dispatch(action);
    private void OpenManager()
    {
        try
        {
            var path=Path.Combine(AppContext.BaseDirectory,"Pulse.exe");
            if(!File.Exists(path))path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../../../src-tauri/target/debug/pulse-manager.exe"));
            if(!File.Exists(path))throw new FileNotFoundException("找不到管理端，请使用完整安装或运行开发管理端。",path);
            Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
        }
        catch(Exception e){log.Write(e);tray?.Balloon("打开管理端失败",e.Message);}
    }
    public int Run() => NativeWindow.MessageLoop();
    public object Metrics => new {orbCount=orbs.Count,sharedDevices=1,sharedAnimationTimers=1,hidden,
        visibleOrbs=orbs.Values.Count(x=>x.Orb.Visible),configuredFps=settings.AnimationMode=="economy"?15:30,
        feedbackSamples=feedbackMs.Count,meanFeedbackMs=feedbackMs.Count==0?0:feedbackMs.Average(),maxFeedbackMs=feedbackMs.Count==0?0:feedbackMs.Max(),
        p95FeedbackMs=feedbackMs.Count==0?0:feedbackMs.Order().ElementAt((int)Math.Ceiling(feedbackMs.Count*.95)-1)};
    public void Dispose()
    {
        disposed=true;previewRenderer?.Dispose();pipe.Dispose();tray?.Dispose();NativeWindow.KillTimer(dispatcher,1);NativeWindow.KillTimer(dispatcher,3);
        foreach(var item in orbs.Values)item.Orb.Dispose();NativeWindow.Destroy(dispatcher);store.Dispose();device.Dispose();
        singleton.ReleaseMutex();singleton.Dispose();
    }
    [DllImport("user32.dll",SetLastError=true)]private static extern bool PostMessageW(nint hwnd,uint message,nuint wp,nint lp);
}
