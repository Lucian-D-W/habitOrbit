using System.Text.Json;
using Pulse.Core;

namespace Pulse.Host;
internal sealed partial class HostRuntime
{
    private DayRecording SetGroups(Guid requestId,JsonElement p)
    {
        Rules.Require(!maintenance,"maintenance","正在恢复备份，暂不接受分组修改。");
        var id=RpcJson.Id(p);
        var cuts=p.GetProperty("cuts").Deserialize<long[]>(RpcJson.Options)??throw new PulseException("invalid_divider","隔片参数无效。");
        var result=Core.SetGroups(id,requestId,RpcJson.Day(p,"date"),RpcJson.Property(p,"expectedVersion").GetString()!,cuts);
        try{Refresh(id);}catch(Exception e){log.Write(e);}
        Changed(id,result.Date);return result;
    }
}
