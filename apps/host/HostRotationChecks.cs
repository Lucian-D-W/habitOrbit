using Pulse.Core;
using Pulse.Desktop;

namespace Pulse.Host;

internal sealed partial class HostRuntime
{
    internal double CheckRotationMultiplier(Guid id)=>orbs[id].Orb.Identity.RotationMultiplier;
}

internal static partial class HostChecks
{
    private static async Task CheckRotation(Client client,HostRuntime runtime,Guid id,ActivityConfig config,string directory)
    {
        var period=await runtime.CheckOnThread(()=>runtime.CheckRotationMultiplier(id));
        Require(period==.85,"预分配倍率未用于桌面绘制");
        var revised=config with{Name="改名不改转速",Visual=config.Visual with{DecorativeSeed=789,Palette=["#AA2233","#3344AA","#FFDDAA"]}};
        Result(await client.Call("activity.update",new{activityId=id,config=revised}));
        Require(await runtime.CheckOnThread(()=>runtime.CheckRotationMultiplier(id))==period,"名称或外观修改改变转速");
        Result(await client.Call("activity.update",new{activityId=id,config}));
        var file=new SettingsFile(Path.Combine(directory,"desktop-settings.json"),e=>throw e);
        Require(file.Load().OrbRotationMultipliers[id]==period,"修改星球覆盖已保存转速");
    }
}
