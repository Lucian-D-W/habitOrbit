using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pulse.Core;
using Pulse.Desktop;

namespace Pulse.Host;

internal static partial class HostChecks
{
    private sealed class Clock:IClock
    {
        private readonly object gate=new();private DateTimeOffset value=new(2026,10,4,12,0,0,TimeSpan.FromHours(8));
        public DateTimeOffset Now{get{lock(gate)return value;}set{lock(gate)this.value=value;}}
    }
    private sealed class Client:IDisposable
    {
        public readonly List<JsonElement> Notifications=new();
        private readonly NamedPipeClientStream pipe;
        private readonly PipeServer.FrameReader reader;
        public Client(string name){pipe=new(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);pipe.Connect(5000);reader=new(pipe);}
        public async Task<JsonElement> Call(string method,object? parameters=null,Guid? id=null)
        {
            var uuid=(id??Guid.NewGuid()).ToString();var frame=JsonSerializer.SerializeToUtf8Bytes(new{id=uuid,method,@params=parameters??new{}},RpcJson.Options);
            await pipe.WriteAsync(frame);await pipe.WriteAsync(new byte[]{10});await pipe.FlushAsync();
            using var timeout=new CancellationTokenSource(10000);
            while(true)
            {
                var line=await reader.Read(timeout.Token)??throw new IOException("Host断开。");using var doc=JsonDocument.Parse(line);
                if(doc.RootElement.TryGetProperty("id",out var responseId) && responseId.GetString()==uuid)return doc.RootElement.Clone();
                if(doc.RootElement.TryGetProperty("method",out _))Notifications.Add(doc.RootElement.Clone());
            }
        }
        public void Dispose()=>pipe.Dispose();
        public async Task<JsonElement> Notice(string method)
        {
            using var timeout=new CancellationTokenSource(10000);
            while(true)
            {
                var line=await reader.Read(timeout.Token)??throw new IOException("通知前连接已关闭。");
                using var doc=JsonDocument.Parse(line);
                if(doc.RootElement.TryGetProperty("method",out var name) && name.GetString()==method)return doc.RootElement.Clone();
            }
        }
    }
    private static void Require(bool value,string check){if(!value)throw new InvalidOperationException(check);}
    private static JsonElement Result(JsonElement response){Require(response.GetProperty("ok").GetBoolean(),response.ToString());return response.GetProperty("result");}
    private static long Amount(JsonElement response)=>Result(response).GetProperty("summary").GetProperty("amount_atoms").GetInt64FromText();
    public static int Run(string artifacts)
    {
        artifacts=Path.GetFullPath(artifacts);Directory.CreateDirectory(artifacts);
        var directory=Path.Combine(artifacts,"isolated-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        var checks=new List<string>();var clock=new Clock();var activityId=Guid.NewGuid();var replayId=Guid.NewGuid();
        var rotationSettings=DesktopSettings.Default;rotationSettings.OrbRotationMultipliers[activityId]=.85;
        new SettingsFile(Path.Combine(directory,"desktop-settings.json"),e=>throw e).Save(rotationSettings);
        var config=new ActivityConfig("隔离记录测试","book","","count","次",1000,4000,new(),VisualSpec.Default);
        object? metrics=null;
        using(var runtime=new HostRuntime(directory,clock,true))
        {
            var task=Task.Run(async()=>
            {
                using var client=new Client(runtime.PipeName);
                try
                {
                    Require(!(await client.Call("host.exit",id:Guid.Empty)).GetProperty("ok").GetBoolean(),"无效退出请求未拒绝");
                    Result(await client.Call("activity.create",new{config},activityId));
                    await CheckRotation(client,runtime,activityId,config,directory);
                    Result(await client.Call("state.subscribe"));
                    for(var i=0;i<35;i++)Result(await client.Call("count.add",new{activityId}));
                    Require(Amount(await client.Call("count.add",new{activityId},replayId))==36000,"连续点击丢失");
                    Require(Amount(await client.Call("count.add",new{activityId},replayId))==36000,"重放重复记录");
                    Require(Amount(await client.Call("count.decrement",new{activityId}))==35000,"撤销原量失败");
                    Require(client.Notifications.Any(n=>n.GetProperty("method").GetString()=="state.changed"),"订阅通知未到达");
                    checks.Add("36 unique adds / repeated UUID / undo / pipe framing");
                    checks.Add("subscribed committed-state notifications received");
                    var recording=Result(await client.Call("recording.get",new{activityId,date="2026-10-04"}));
                    Result(await client.Call("recording.set_groups",new{activityId,date="2026-10-04",expectedVersion=recording.GetProperty("version").GetString(),cuts=new long[]{10}}));
                    using(var locked=new SqliteConnection("Data Source="+Path.Combine(directory,"pulse.db")))
                    {
                        locked.Open();using var query=locked.CreateCommand();query.CommandText="BEGIN IMMEDIATE";query.ExecuteNonQuery();
                        var failed=await client.Call("count.add",new{activityId});
                        Require(!failed.GetProperty("ok").GetBoolean() && failed.GetProperty("error").GetProperty("code").GetString()=="storage_busy","写入失败冒充成功");
                        query.CommandText="ROLLBACK";query.ExecuteNonQuery();
                    }
                    var detail=Result(await client.Call("stats.day_detail",new{activityId,date="2026-10-04"}));
                    Require(detail.GetProperty("summary").GetProperty("amount_atoms").GetInt64FromText()==35000,"失败写入改变事实");
                    checks.Add("database busy rejects without phantom success or count");
                    Result(await client.Call("desktop.set_visible",new{activityId,visible=false}));
                    Result(await client.Call("desktop.set_visible",new{activityId,visible=false}));
                    var saved=new SettingsFile(Path.Combine(directory,"desktop-settings.json"),e=>throw e).Load();
                    Require(!saved.Orbs[activityId].Visible,"显隐请求变成toggle");
                    checks.Add("explicit visibility request replay / atomic settings");
                    var before=File.ReadAllBytes(Path.Combine(directory,"desktop-settings.json"));
                    await runtime.CheckOnThread(()=>{runtime.ToggleTemporary();return true;});
                    await runtime.CheckOnThread(()=>{runtime.ToggleTemporary();return true;});
                    Require(File.ReadAllBytes(Path.Combine(directory,"desktop-settings.json")).SequenceEqual(before),"临时隐藏修改永久显隐");
                    checks.Add("temporary tray hide/restore leaves persistent settings bytes unchanged");
                    var otherId=Guid.NewGuid();var otherConfig=config with {Name="第二颗",Visual=VisualSpec.Default with {AccentColor="#43DAAA"}};
                    Result(await client.Call("activity.create",new{config=otherConfig},otherId));
                    var otherPeriod=await runtime.CheckOnThread(()=>runtime.CheckRotationMultiplier(otherId));
                    Require(otherPeriod>=.85 && otherPeriod<=1.5 && new SettingsFile(Path.Combine(directory,"desktop-settings.json"),e=>throw e).Load().OrbRotationMultipliers[otherId]==otherPeriod,"新星球倍率未分配并保存");
                    Require(await runtime.CheckOnThread(()=>runtime.CheckRotationMultiplier(activityId))==.85,"记录或成熟度变化改变倍率");
                    checks.Add("assigned desktop rotation / rename and appearance edits retain speed / new orb speed persisted in range");
                    await runtime.CheckOnThread(()=>
                    {
                        try{using var duplicate=new HostRuntime(directory,clock,true);throw new InvalidOperationException("双写入者未被拒绝");}
                        catch(PulseException e)when(e.Code=="already_running"){return true;}
                    });
                    checks.Add("multiple orbs share Host / second writer rejected");
                    var page=Result(await client.Call("stats.day_detail",new{activityId,date="2026-10-04",offset=0,limit=2}));
                    Require(page.GetProperty("event_count").GetInt32()==37 && page.GetProperty("events").GetArrayLength()==2 && page.GetProperty("has_more").GetBoolean(),"事件分页缺失");
                    var largeId=Guid.NewGuid();var largeConfig=config with {Name="精度",StepAtoms=9007199254740993,Visual=VisualSpec.Default with {AccentColor="#FA5667"}};
                    Result(await client.Call("activity.create",new{config=largeConfig},largeId));
                    Require(Amount(await client.Call("count.add",new{activityId=largeId}))==9007199254740993,"64位精度丢失");
                    checks.Add("event pagination retains full summary / integers beyond 2^53 transported exactly");
                    clock.Now=new(2026,10,5,0,0,1,TimeSpan.FromHours(8));
                    var snapshot=Result(await client.Call("state.snapshot"));
                    Require(snapshot.GetProperty("activities").EnumerateArray().All(a=>a.GetProperty("today").GetProperty("amount_atoms").GetInt64FromText()==0),"午夜显示未归零");
                    Require(Result(await client.Call("count.decrement",new{activityId})).GetProperty("status").GetString()=="no_change","跨日撤销");
                    checks.Add("midnight refresh preserves history / does not undo previous date");
                    var deleteId=Guid.NewGuid();var deleteConfig=config with{Name="删除专用隔离星球",StepAtoms=125,Visual=config.Visual with{AccentColor="#123456"}};
                    Result(await client.Call("activity.create",new{config=deleteConfig},deleteId));
                    Result(await client.Call("count.add",new{activityId=deleteId}));
                    Require(!(await client.Call("count.set_today",new{activityId=deleteId,date="2026-10-05",amount="3.125",confirmed=false})).GetProperty("ok").GetBoolean(),"未确认改动今日总量");
                    var amountRequest=Guid.NewGuid();
                    Require(Amount(await client.Call("count.set_today",new{activityId=deleteId,date="2026-10-05",amount="3.125",confirmed=true},amountRequest))==3125,"总量编辑精度丢失");
                    Require(Amount(await client.Call("count.set_today",new{activityId=deleteId,date="2026-10-05",amount="3.125",confirmed=true},amountRequest))==3125,"总量编辑重放重复追加");
                    Require(Amount(await client.Call("count.decrement",new{activityId=deleteId}))==3000,"总量调整未按一颗份量撤销");
                    await CheckRecording(client,runtime,deleteId);
                    checks.Add("desktop counting / live bead partitions / snapshot and statistics consistency / stale draft rejection / idempotent group retry / tail cleanup");
                    Require(Amount(await client.Call("count.set_today",new{activityId=deleteId,date="2026-10-05",amount="0",confirmed=true}))==0,"总量归零失败");
                    var edited=Result(await client.Call("stats.day_detail",new{activityId=deleteId,date="2026-10-05"}));
                    Require(edited.GetProperty("summary").GetProperty("amount_atoms").GetInt64FromText()==0,"编辑后的统计不一致");
                    checks.Add("confirmed today-total edit / decimal precision / request replay / single-portion undo / zero agrees with statistics");
                    Require(!(await client.Call("activity.delete",new{activityId=deleteId,confirmed=true})).GetProperty("ok").GetBoolean(),"未归档允许删除");
                    Result(await client.Call("activity.archive",new{activityId=deleteId}));
                    Require(!(await client.Call("activity.delete",new{activityId=deleteId,confirmed=false})).GetProperty("ok").GetBoolean(),"未确认允许删除");
                    Result(await client.Call("activity.get",new{activityId=deleteId}));
                    Result(await client.Call("activity.delete",new{activityId=deleteId,confirmed=true}));
                    Require(!(await client.Call("activity.get",new{activityId=deleteId})).GetProperty("ok").GetBoolean(),"删除后仍有详情");
                    var remaining=Result(await client.Call("state.snapshot")).GetProperty("activities").EnumerateArray().Select(x=>x.GetProperty("activity").GetProperty("id").GetGuid()).ToHashSet();
                    Require(remaining.SetEquals(snapshot.GetProperty("activities").EnumerateArray().Select(x=>x.GetProperty("activity").GetProperty("id").GetGuid())),"删除影响其他星球或留下列表残影");
                    checks.Add("archived delete requires confirmation / full snapshot removes target / other Activities retained");
                    metrics=Result(await client.Call("host.metrics")).Clone();
                }
                finally{await client.Call("host.exit");}
            });
            runtime.Run();task.GetAwaiter().GetResult();
        }
        using(var runtime=new HostRuntime(directory,clock,true))
        {
            var task=Task.Run(async()=>
            {
                using var client=new Client(runtime.PipeName);
                try
                {
                    Require(Amount(await client.Call("count.add",new{activityId},replayId))==36000,"重启重放丢失原响应");
                    Require(await runtime.CheckOnThread(()=>runtime.CheckRotationMultiplier(activityId))==.85,"重启改变已分配倍率");
                    checks.Add("reopened desktop renderer uses the same saved rotation period");
                    var detail=Result(await client.Call("stats.day_detail",new{activityId,date="2026-10-04"}));
                    Require(detail.GetProperty("summary").GetProperty("amount_atoms").GetInt64FromText()==35000,"重启事实不一致");
                    Require(detail.GetProperty("groups").GetArrayLength()==2,"重启后手动隔片丢失");
                    var snapshot=Result(await client.Call("state.snapshot"));
                    Require(!snapshot.GetProperty("activities").EnumerateArray().Single(a=>a.GetProperty("activity").GetProperty("id").GetGuid()==activityId).GetProperty("desktop").GetProperty("visible").GetBoolean(),"重启显隐丢失");
                    checks.Add("reopen database / original UUID response / saved visibility");
                }
                finally{await client.Call("host.exit");}
            });runtime.Run();task.GetAwaiter().GetResult();
        }
        var consent=new ExitCoordinator();var one=Guid.NewGuid();var two=Guid.NewGuid();
        var request=consent.Start([one,two]);Require(consent.Vote(request,one,true)=="waiting","未等所有管理窗口同意");
        Require(consent.Vote(request,two,false)=="cancelled" && consent.RequestId is null,"取消退出未保留应用");
        var retry=consent.Start([one]);Require(retry!=request && consent.Vote(request,one,true)=="stale","旧请求能关闭新会话");
        Require(consent.Disconnect(one) && !consent.Committed,"普通断线被当作退出同意");
        Require(consent.Start([])!=Guid.Empty && consent.Committed,"无管理窗口不能退出");
        using(var runtime=new HostRuntime(directory,clock,true))
        {
            var task=Task.Run(async()=>
            {
                using var observer=new Client(runtime.PipeName);using var manager=new Client(runtime.PipeName);var session=Guid.NewGuid();
                Result(await observer.Call("state.subscribe"));Result(await manager.Call("state.subscribe",new{managerSession=session}));
                var first=Result(await observer.Call("host.exit"));Require(!first.GetProperty("exiting").GetBoolean(),"Host忽略管理窗口直接退出");
                var notice=await manager.Notice("host.exit_requested");var token=notice.GetProperty("params").GetProperty("request_id").GetGuid();
                var cancelled=Result(await manager.Call("host.exit_reply",new{requestId=token,managerSession=session,allow=false}));
                Require(cancelled.GetProperty("decision").GetString()=="cancelled","管理窗口取消无效");
                Result(await observer.Call("state.snapshot"));
                Require(!(await manager.Call("host.exit_reply",new{requestId=token,managerSession=session,allow=true})).GetProperty("ok").GetBoolean(),"失效确认仍能退出");
                Result(await observer.Call("host.exit"));notice=await manager.Notice("host.exit_requested");token=notice.GetProperty("params").GetProperty("request_id").GetGuid();
                Require(Result(await manager.Call("host.exit_reply",new{requestId=token,managerSession=session,allow=true})).GetProperty("exiting").GetBoolean(),"明确同意未提交退出");
                await manager.Notice("host.exiting");
                checks.Add("coordinated exit / manager cancellation keeps Host alive / stale consent rejected / explicit shutdown signal flushed");
            });
            runtime.Run();task.GetAwaiter().GetResult();
        }
        var displays=DesktopLayout.Displays();var point=DesktopLayout.FreePosition(displays,[]);
        var display=DesktopLayout.Nearest(displays,point);Require(display.Restore(display.Save(point,true))==point,"DIP位置往返");
        CheckCountingProjection(config,directory);
        checks.Add("display device ID / relative DIP roundtrip / ten-record satellites and remainder stars for both grouping settings / undo and idle invariance");
        using(var oversized=new MemoryStream(Encoding.UTF8.GetBytes(new string('水',PipeServer.MaxBytes/3+1)+"\n")))
        {
            try{new PipeServer.FrameReader(oversized).Read(CancellationToken.None).GetAwaiter().GetResult();throw new InvalidOperationException("字节上限未执行");}
            catch(IOException){checks.Add("UTF-8 byte limit rejects oversized frame before parsing");}
        }
        var invalidSettings=Path.Combine(directory,"invalid-settings.json");File.WriteAllText(invalidSettings,"{not-json");var failures=0;
        Require(new SettingsFile(invalidSettings,_=>failures++).Load().Orbs.Count==0 && failures==1,"损坏配置未恢复默认");
        using(var reopened=new Pulse.Storage.SqliteStore(Path.Combine(directory,"pulse.db")))
            Require(new PulseCore(reopened,clock).Detail(activityId,new(2026,10,4)).Summary.AmountAtoms==35000,"配置损坏影响事实");
        checks.Add("corrupt settings fallback leaves recorded database facts intact");
        var report=new{checks,metrics,isolatedDirectory=directory,uiAcceptance="not performed",trayAcceptance="human pending"};
        File.WriteAllText(Path.Combine(artifacts,"host-checks.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));return 0;
    }
    private static void CheckCountingProjection(ActivityConfig config,string directory)
    {
        foreach(var grouped in new[]{false,true})
        {
            using var storage=new Pulse.Storage.SqliteStore(Path.Combine(directory,$"projection-{grouped}.db"));
            var time=new Clock();var core=new PulseCore(storage,time);
            var activity=core.Create(Guid.NewGuid(),config with{GroupGapMs=grouped?4000:null});
            var date=DateOnly.FromDateTime(time.Now.DateTime);
            void Check(long count,long satellites,long stars)
            {
                var detail=core.Detail(activity.Id,date);
                foreach(var seconds in new[]{0,6,60})
                {
                    var state=OrbProjection.State(activity,detail,time.Now.AddSeconds(seconds),.6f);
                    Require(state.GroupSatellites is null && state.FeedbackCount==count,"分组覆盖了有效记录份数");
                    Require(state.FeedbackCount/10==satellites && state.FeedbackCount%10==stars,"十合一或余数错误");
                    Require(state.Energy==.6f && state.CoreScale==.7f+.1f*.6f,"数量修正改变成长外观");
                }
            }
            for(var count=1;count<=35;count++)
            {
                core.Add(activity.Id,Guid.NewGuid());
                if(count==9)Check(9,0,9);
                if(count==10)Check(10,1,0);
                if(count==11)Check(11,1,1);
            }
            Check(35,3,5);
            Require(core.Detail(activity.Id,date).Summary.AmountAtoms==35*config.StepAtoms,"固定每份实际量应保持计份关系");
            Require(core.Detail(activity.Id,date).Groups.Count==(grouped?1:0),"数量展示修正改变了分组事实");
            for(var count=34;count>=9;count--)
            {
                core.Decrement(activity.Id,Guid.NewGuid());
                if(count==11)Check(11,1,1);
                if(count==10)Check(10,1,0);
                if(count==9)Check(9,0,9);
            }
        }
    }
    private static long GetInt64FromText(this JsonElement value)=>value.ValueKind==JsonValueKind.String?long.Parse(value.GetString()!):value.GetInt64();
}
