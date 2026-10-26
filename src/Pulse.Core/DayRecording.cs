using System.Security.Cryptography;
using System.Text.Json;

namespace Pulse.Core;

public sealed record PortionRun(long Start,long Count,long AmountAtoms,long EventNo);
public sealed record DayPartition(long[] Cuts,bool Manual,long? Gap);
public sealed record DayRecording(DateOnly Date,long AmountAtoms,long Units,long StepAtoms,string Version,
    long[] Cuts,PortionRun[] Runs,GroupSummary[] Groups,bool Grouped,bool Manual);
public sealed record GroupReceipt(Guid Id,Guid ActivityId,string Input,DayRecording Result);

public sealed partial class PulseCore
{
    public DayRecording Recording(Guid id,DateOnly day)=>store.Transaction(tx=>ReadRecording(tx,Get(tx,id),day,tx.DayEvents(id,day)));

    public DayRecording SetGroups(Guid id,Guid requestId,DateOnly day,string expectedVersion,long[] cuts)=>store.Transaction(tx=>
    {
        Rules.Require(requestId!=Guid.Empty,"invalid_id","请求UUID无效。");
        var input=JsonSerializer.Serialize(new{day,expectedVersion,cuts});
        if(tx.GetGroupReceipt(requestId) is GroupReceipt receipt)
        {
            Rules.Require(receipt.ActivityId==id && receipt.Input==input,"request_conflict","相同UUID对应不同分组参数。");
            return receipt.Result;
        }
        Rules.Require(tx.GetReceipt(requestId) is null,"request_conflict","相同UUID已用于数量操作。");
        var activity=Get(tx,id);
        Rules.Require(activity.ArchivedAtMs is null,"archived","归档星球不能修改分组。");
        Rules.Require(day==DateOnly.FromDateTime(clock.Now.DateTime),"date_changed","日期已变化，请重新载入当日记录。");
        var events=tx.DayEvents(id,day);var before=ReadRecording(tx,activity,day,events);
        Rules.Require(before.Version==expectedVersion,"recording_conflict","记录已变化，请重新载入后调整隔片。");
        Rules.Require(cuts.Length<=10000 && cuts.All(x=>x>0 && x<before.Units) && cuts.SequenceEqual(cuts.Distinct().Order()),"invalid_divider","隔片须在两颗珠子之间，不能重复或形成空组。");
        tx.SavePartition(id,day,new(cuts,true,activity.Config.GroupGapMs));
        var result=ReadRecording(tx,activity,day,events);
        tx.InsertGroupReceipt(new(requestId,id,input,result));return result;
    });

    private static DayRecording ReadRecording(ITransaction tx,Activity activity,DateOnly day,IReadOnlyList<RecordEvent> events)
    {
        var reversed=events.Where(e=>e.ReversesId is not null).Select(e=>e.ReversesId!.Value).ToHashSet();
        var runs=new List<PortionRun>();long units=0,amount=0;
        foreach(var e in events.Where(e=>e.DeltaSteps==1 && !reversed.Contains(e.Id)).OrderBy(e=>e.EventNo))
        {
            Rules.Require(e.DeltaUnits>0 && e.DeltaAmountAtoms%e.DeltaUnits==0,"invalid_portions","记录份量不一致，请检查数据。");
            runs.Add(new(units,e.DeltaUnits,e.DeltaAmountAtoms/e.DeltaUnits,e.EventNo));
            units=checked(units+e.DeltaUnits);amount=checked(amount+e.DeltaAmountAtoms);
        }
        var partition=tx.GetPartition(activity.Id,day);
        long[] cuts;
        if(partition is not null && (partition.Manual || partition.Gap==activity.Config.GroupGapMs))cuts=partition.Cuts.Where(x=>x>0 && x<units).ToArray();
        else if(activity.Config.GroupGapMs is long gap)
        {
            long position=0;var automatic=new List<long>();
            foreach(var group in Rules.Groups(events,gap)){position=checked(position+group.RecordedUnits);if(position<units)automatic.Add(position);}
            cuts=automatic.ToArray();
        }
        else cuts=[];
        var groups=new List<GroupSummary>();long start=0;var runIndex=0;
        foreach(var end in cuts.Append(units))
        {
            if(end<=start)continue;
            long total=0,first=0,last=0;
            var cursor=start;
            while(cursor<end)
            {
                var run=runs[runIndex];var until=Math.Min(end,run.Start+run.Count);var count=until-cursor;
                if(first==0)first=run.EventNo;last=run.EventNo;total=checked(total+count*run.AmountAtoms);
                cursor=until;if(cursor==run.Start+run.Count)runIndex++;
            }
            groups.Add(new(first,last,total,end-start));start=end;
        }
        var manual=partition?.Manual??false;
        var version=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{activity.Id,day,activity.Config.StepAtoms,activity.Config.GroupGapMs,
            last=events.LastOrDefault()?.EventNo??0,amount,units,cuts,manual})));
        return new(day,amount,units,activity.Config.StepAtoms,version,cuts,runs.ToArray(),groups.ToArray(),manual || activity.Config.GroupGapMs is not null,manual);
    }

    private static void SaveAfterQuantity(ITransaction tx,Activity a,DateOnly day,DayRecording before,IReadOnlyList<RecordEvent> previousEvents,bool added,DateTimeOffset now)
    {
        var after=ReadRecording(tx,a,day,tx.DayEvents(a.Id,day));
        var cuts=before.Cuts.Where(x=>x<after.Units).ToList();
        if(added && before.Units>0 && a.Config.GroupGapMs is long gap)
        {
            var previous=previousEvents.LastOrDefault(e=>e.DeltaSteps==1);
            var elapsed=previous is null?0:now.ToUnixTimeMilliseconds()-previous.RecordedAtMs;
            if(previous is not null && (elapsed<0 || elapsed>gap) && !cuts.Contains(before.Units))cuts.Add(before.Units);
        }
        tx.SavePartition(a.Id,day,new(cuts.Order().ToArray(),before.Manual,a.Config.GroupGapMs));
    }
}
