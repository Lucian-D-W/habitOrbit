using System.Text.Json;
using Pulse.Core;
namespace Pulse.Host;
internal sealed partial class HostRuntime
{
    private GalleryLayout Gallery()=>store.ReadGallery().Normalize(Core.List(true).Select(a=>a.Id).ToArray());
    private GalleryLayout SaveGallery(JsonElement p)
    {
        Rules.Require(!maintenance,"maintenance","正在恢复备份，暂不接受布局修改。");
        var layout=p.GetProperty("layout").Deserialize<GalleryLayout>(RpcJson.Options)??throw new PulseException("invalid_layout","布局为空。");
        var saved=store.SaveGallery(layout,Core.List(true).Select(a=>a.Id).ToArray());Changed(null,Today);return saved;
    }
}
