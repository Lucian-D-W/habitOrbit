using Pulse.Core;

namespace Pulse.Host;
internal static partial class HostChecks
{
    private static async Task CheckRecording(Client client,HostRuntime runtime,Guid activityId)
    {
        const string date="2026-10-05";
        Result(await client.Call("count.set_today",new{activityId,date,amount="1",confirmed=true}));
        var r=Result(await client.Call("recording.get",new{activityId,date}));
        Require(r.GetProperty("units").GetInt64FromText()==8,"批量改数字未展开为8颗");
        var request=Guid.NewGuid();var parameters=new{activityId,date,expectedVersion=r.GetProperty("version").GetString(),cuts=new long[]{3}};
        var grouped=Result(await client.Call("recording.set_groups",parameters,request));
        Require(grouped.GetProperty("groups")[0].GetProperty("amount_atoms").GetInt64FromText()==375 && grouped.GetProperty("groups")[1].GetProperty("amount_atoms").GetInt64FromText()==625,"3+5分组实际量不符");
        // Invoke the exact callback path used by DesktopOrb; this is a technical integration check, not UI automation.
        await runtime.CheckOnThread(()=>runtime.Count(activityId,Guid.NewGuid(),true,true));
        var replay=Result(await client.Call("recording.set_groups",parameters,request));
        Require(replay.GetProperty("version").GetString()==grouped.GetProperty("version").GetString(),"分组重试未返回原结果");
        var latest=Result(await client.Call("recording.get",new{activityId,date}));
        Require(latest.GetProperty("units").GetInt64FromText()==9,"旧分组重试覆盖桌面新增");
        var stale=await client.Call("recording.set_groups",new{activityId,date,expectedVersion=grouped.GetProperty("version").GetString(),cuts=new long[]{4}});
        Require(!stale.GetProperty("ok").GetBoolean() && stale.GetProperty("error").GetProperty("code").GetString()=="recording_conflict","旧草稿未拒绝");
        var row=Result(await client.Call("state.activity",new{activityId})).GetProperty("row");
        Require(row.GetProperty("today").GetProperty("amount_atoms").GetInt64FromText()==1125 && row.GetProperty("today_groups")[1].GetProperty("recorded_units").GetInt64FromText()==6,"快照未同步分组与数量");
        Require(row.GetProperty("today_version").GetString()==latest.GetProperty("version").GetString(),"跨模块版本不一致");
        Result(await client.Call("count.set_today",new{activityId,date,amount="0.75",confirmed=true}));
        var reduced=Result(await client.Call("recording.get",new{activityId,date}));
        Require(reduced.GetProperty("cuts").GetArrayLength()==1 && reduced.GetProperty("groups")[1].GetProperty("recorded_units").GetInt64FromText()==3,"尾组减量错误");
        Result(await client.Call("count.set_today",new{activityId,date,amount="0.375",confirmed=true}));
        var tail=Result(await client.Call("stats.day_detail",new{activityId,date}));
        Require(tail.GetProperty("groups").GetArrayLength()==1 && tail.GetProperty("summary").GetProperty("recorded_units").GetInt64FromText()==3,"尾组清空未同步统计");
        Require(client.Notifications.Count(n=>n.GetProperty("method").GetString()=="state.changed")>5,"提交未发送同步通知");
    }
}
