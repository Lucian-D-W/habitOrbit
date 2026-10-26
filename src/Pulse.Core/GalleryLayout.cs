namespace Pulse.Core;

public sealed record GalleryGroup(Guid Id,string Name,IReadOnlyList<Guid> ActivityIds);
public sealed record GalleryLayout(long Revision,IReadOnlyList<Guid> Ungrouped,IReadOnlyList<GalleryGroup> Groups)
{
    public IReadOnlyList<Guid> Favorites {get;init;}=Array.Empty<Guid>();
    // Null is an older document; normalize its order in memory until the next
    // ordinary revision-checked save. No database migration is required.
    public IReadOnlyList<string>? TopOrder {get;init;}
    public static GalleryLayout Empty=>new(0,Array.Empty<Guid>(),Array.Empty<GalleryGroup>());
    public GalleryLayout Normalize(IReadOnlyList<Guid> existing)
    {
        var allowed=existing.ToHashSet();var seen=new HashSet<Guid>();
        Guid[] Clean(IEnumerable<Guid> ids)=>ids.Where(id=>allowed.Contains(id)&&seen.Add(id)).ToArray();
        var ungrouped=Clean(Ungrouped).ToList();var groupIds=new HashSet<Guid>();
        var groups=Groups.Where(g=>g.Id!=Guid.Empty&&groupIds.Add(g.Id)).Select(g=>g with{ActivityIds=Clean(g.ActivityIds)}).ToArray();
        ungrouped.AddRange(existing.Where(id=>seen.Add(id)));
        var expected=ungrouped.Select(id=>$"card:{id}").Concat(groups.Select(g=>$"group:{g.Id}")).ToArray();
        var valid=expected.ToHashSet(StringComparer.Ordinal);var ordered=new HashSet<string>(StringComparer.Ordinal);
        var top=(TopOrder??Array.Empty<string>()).Where(key=>valid.Contains(key)&&ordered.Add(key)).ToList();
        top.AddRange(expected.Where(ordered.Add));
        return this with{Ungrouped=ungrouped,Groups=groups,Favorites=Favorites.Where(allowed.Contains).Distinct().ToArray(),TopOrder=top};
    }
    public void Validate(IReadOnlyList<Guid> existing)
    {
        Rules.Require(Groups.Count<=1000&&Groups.Select(g=>g.Id).Distinct().Count()==Groups.Count,"invalid_layout","分组重复或数量过多。");
        Rules.Require(Groups.All(g=>g.Id!=Guid.Empty&&g.Name.Trim().Length is >=1 and <=64),"invalid_layout","分组名称需为1至64字。");
        var ids=Ungrouped.Concat(Groups.SelectMany(g=>g.ActivityIds)).ToArray();
        Rules.Require(ids.Length==ids.Distinct().Count()&&ids.ToHashSet().SetEquals(existing),"invalid_layout","星球布局有遗漏、重复或已失效，请刷新后再试。");
        Rules.Require(Favorites.Distinct().Count()==Favorites.Count&&Favorites.All(existing.Contains),"invalid_layout","常用星球重复或已失效，请刷新后再试。");
        if(TopOrder is not null)
        {
            var expected=Ungrouped.Select(id=>$"card:{id}").Concat(Groups.Select(g=>$"group:{g.Id}"));
            Rules.Require(TopOrder.Count==TopOrder.Distinct(StringComparer.Ordinal).Count()&&TopOrder.ToHashSet(StringComparer.Ordinal).SetEquals(expected),"invalid_layout","顶层布局有遗漏、重复或已失效，请刷新后再试。");
        }
    }
}
