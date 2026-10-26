using System.Text.Json;
using Pulse.Core;

namespace Pulse.Host;

internal static class HostPreview
{
    public static int Run(string directory)
    {
        using var runtime=new HostRuntime(directory);
        if(runtime.Core.List(true).Count==0)
        {
            var configs=new[]{
                new ActivityConfig("阅读","book-open","持久化开发样例","duration","min",5000,null,new(),VisualSpec.Default),
                new ActivityConfig("喝水","droplets","持久化开发样例","count","杯",500,null,new(),VisualSpec.Default with {
                    BasePreset="nature",Palette=["#45B5C6","#40D7B5","#ADFFCC"],AccentColor="#43DAAA",DecorativeSeed=22}),
                new ActivityConfig("运动","dumbbell","持久化开发样例","count","次",1000,4000,new(),VisualSpec.Default with {
                    BasePreset="sunset",Palette=["#FA5667","#F28C4E","#FFF5A7"],AccentColor="#FFA04B",DecorativeSeed=33})};
            foreach(var config in configs)
            {
                var line=JsonSerializer.Serialize(new{id=Guid.NewGuid(),method="activity.create",@params=new{config}},RpcJson.Options);
                using var response=JsonDocument.Parse(JsonSerializer.Serialize(runtime.Handle(line),RpcJson.Options));
                if(!response.RootElement.GetProperty("ok").GetBoolean())throw new InvalidOperationException(response.RootElement.ToString());
                runtime.Handle(JsonSerializer.Serialize(new{id=Guid.NewGuid(),method="desktop.set_visible",@params=new{activityId=response.RootElement.GetProperty("result").GetProperty("id").GetGuid(),visible=true}},RpcJson.Options));
            }
        }
        return runtime.Run();
    }
}
