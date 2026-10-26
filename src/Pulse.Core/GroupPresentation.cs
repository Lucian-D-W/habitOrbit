namespace Pulse.Core;

public static class GroupPresentation
{
    public static string Describe(IReadOnlyList<GroupSummary> groups,string unit)
    {
        var total=groups.Aggregate(0L,(sum,g)=>checked(sum+g.AmountAtoms));
        var average=groups.Count==0?0L:(long)decimal.Round((decimal)total/groups.Count,0,MidpointRounding.AwayFromZero);
        var amounts=groups.Count==0?"暂无分组":string.Join(" / ",groups.Select(g=>Rules.FormatAtoms(g.AmountAtoms)));
        return $"{amounts}\n{groups.Count}组 × 平均{Rules.FormatAtoms(average)}{unit}，共{Rules.FormatAtoms(total)}{unit}";
    }
}
