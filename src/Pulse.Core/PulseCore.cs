using System.Text.Json;

namespace Pulse.Core;

public sealed partial class PulseCore(IStore store, IClock clock)
{
    private static Activity Get(ITransaction tx, Guid id) => tx.GetActivity(id) ?? throw new PulseException("not_found","星球不存在。");
    public Activity Create(Guid requestId, ActivityConfig input) => store.Transaction(tx=>
    {
        Rules.Require(requestId!=Guid.Empty,"invalid_id","请求UUID无效。");
        var config=Rules.Normalize(input);
        var existing=tx.GetActivity(requestId);
        if(existing is not null)
        {
            Rules.Require(JsonSerializer.Serialize(config,Rules.Json)==JsonSerializer.Serialize(existing.Config,Rules.Json),"request_conflict","相同UUID对应不同创建参数。");
            return existing;
        }
        var now=clock.Now;
        var activity=new Activity(requestId,config,Rules.VisualKey(config.Visual),now.ToUnixTimeMilliseconds(),DateOnly.FromDateTime(now.DateTime),now.ToUnixTimeMilliseconds(),null);
        tx.InsertActivity(activity); return activity;
    });
    public Activity Update(Guid id, ActivityConfig input) => store.Transaction(tx=>
    {
        var activity=Get(tx,id); var config=Rules.Normalize(input);
        Rules.Require(config.Kind==activity.Config.Kind && config.Unit==activity.Config.Unit && config.StepAtoms==activity.Config.StepAtoms,
            "measurement_locked","创建后的单位和每次记录量不能改换，需要不同记录方式请新建星球。");
        var changed=activity with {Config=config,VisualKey=Rules.VisualKey(config.Visual),UpdatedAtMs=clock.Now.ToUnixTimeMilliseconds()};
        tx.UpdateActivity(changed); return changed;
    });
    public Activity Archive(Guid id, bool archived) => store.Transaction(tx=>
    {
        var activity=Get(tx,id); var now=clock.Now.ToUnixTimeMilliseconds();
        var changed=activity with {ArchivedAtMs=archived?activity.ArchivedAtMs??now:null,UpdatedAtMs=now};
        tx.UpdateActivity(changed); return changed;
    });
    public Activity Activity(Guid id) => store.Transaction(tx=>Get(tx,id));
    public void Delete(Guid id,bool confirmed) => store.Transaction(tx=>
    {
        Rules.Require(confirmed,"confirmation_required","删除星球及全部历史记录需要明确确认。");
        var activity=Get(tx,id);
        Rules.Require(activity.ArchivedAtMs is not null,"not_archived","只有已归档星球可以永久删除。");
        tx.DeleteActivity(id);return true;
    });
    public MaturityState Growth(Guid id, DateOnly today) => store.Transaction(tx=>
    {
        Get(tx,id);
        return Maturity.Evaluate(tx.DailyAggregates(id,DateOnly.MinValue,today),today);
    });
    public IReadOnlyList<Activity> List(bool includeArchived=false) => store.Transaction(tx=>tx.ListActivities(includeArchived));
    public CountResult Add(Guid activityId, Guid eventId) => Count(activityId,eventId,"add");
    public CountResult Decrement(Guid activityId, Guid eventId) => Count(activityId,eventId,"decrement");
    public CountResult SetToday(Guid activityId,Guid requestId,DateOnly day,long target,bool confirmed,string? expectedVersion=null) => store.Transaction(tx=>
    {
        Rules.Require(confirmed,"confirmation_required","修改今日总量需要确认保存。");
        Rules.Require(requestId!=Guid.Empty && target>=0,"invalid_amount","请求或数量无效。");
        if(tx.GetReceipt(requestId) is CommandReceipt receipt)
        {
            Rules.Require(receipt.ActivityId==activityId && receipt.Operation=="set_today" && receipt.Result.RequestedAmountAtoms==target && receipt.Result.Summary.Date==day,"request_conflict","相同UUID对应不同修改参数。");
            return receipt.Result;
        }
        var a=Get(tx,activityId);var now=clock.Now;
        Rules.Require(tx.GetGroupReceipt(requestId) is null,"request_conflict","相同UUID已用于分组操作。");
        Rules.Require(a.ArchivedAtMs is null,"archived","归档星球不能修改今日总量。");
        Rules.Require(day==DateOnly.FromDateTime(now.DateTime),"date_changed","日期已变化，请重新编辑今天的数量。");
        var events=tx.DayEvents(activityId,day);var before=ReadRecording(tx,a,day,events);
        Rules.Require(expectedVersion is null || expectedVersion==before.Version,"recording_conflict","记录已变化，请重新载入后修改数量。");
        var summary=Rules.Daily(a,tx.DailyAggregates(activityId,day,day),day,day,day)[0];
        var amount=summary.AmountAtoms;RecordEvent? record=null;
        Rules.Require(target==amount || target%a.Config.StepAtoms==0,"invalid_portion",$"数量须为每次记录量 {Rules.FormatAtoms(a.Config.StepAtoms)} 的整数倍。");
        if(amount>target)
        {
            var reversed=events.Where(e=>e.ReversesId is not null).Select(e=>e.ReversesId!.Value).ToHashSet();
            foreach(var positive in events.Where(e=>e.DeltaSteps==1 && !reversed.Contains(e.Id)).OrderByDescending(e=>e.EventNo))
            {
                if(amount<=target)break;
                record=tx.InsertEvent(new(0,Guid.NewGuid(),activityId,now.ToUnixTimeMilliseconds(),day,(int)now.Offset.TotalMinutes,-1,-positive.DeltaAmountAtoms,positive.Id){DeltaUnits=-positive.DeltaUnits});
                amount-=positive.DeltaAmountAtoms;
            }
        }
        if(amount<target)
        {
            var difference=target-amount;
            // A surviving legacy remainder retains its actual amount; new batches use fixed portions.
            var units=difference%a.Config.StepAtoms==0?difference/a.Config.StepAtoms:1;
            record=tx.InsertEvent(new(0,Guid.NewGuid(),activityId,now.ToUnixTimeMilliseconds(),day,(int)now.Offset.TotalMinutes,1,difference,null){DeltaUnits=units});
        }
        if(record is not null)SaveAfterQuantity(tx,a,day,before,events,target>before.AmountAtoms,now);
        summary=Rules.Daily(a,tx.DailyAggregates(activityId,day,day),day,day,day)[0];
        var result=new CountResult(record is null?"no_change":"changed",record,summary){RequestedAmountAtoms=target};
        tx.InsertReceipt(new(requestId,activityId,"set_today",result));return result;
    });
    private CountResult Count(Guid activityId, Guid id, string operation) => store.Transaction(tx=>
    {
        Rules.Require(id!=Guid.Empty,"invalid_id","事件UUID无效。");
        var receipt=tx.GetReceipt(id);
        if(receipt is not null)
        {
            Rules.Require(receipt.ActivityId==activityId && receipt.Operation==operation,"request_conflict","相同UUID对应不同计数参数。");
            return receipt.Result;
        }
        var activity=Get(tx,activityId);
        Rules.Require(activity.ArchivedAtMs is null,"archived","归档星球不能增加或撤销记录。");
        Rules.Require(tx.GetGroupReceipt(id) is null,"request_conflict","相同UUID已用于分组操作。");
        var now=clock.Now; var day=DateOnly.FromDateTime(now.DateTime);
        var events=tx.DayEvents(activityId,day);var before=ReadRecording(tx,activity,day,events);
        RecordEvent? record=null;
        Rules.Require(operation!="add" || before.AmountAtoms<=long.MaxValue-activity.Config.StepAtoms,"numeric_limit","累积数量超出支持范围，本次未记录。");
        if(operation=="add") record=new(0,id,activityId,now.ToUnixTimeMilliseconds(),day,(int)now.Offset.TotalMinutes,1,activity.Config.StepAtoms,null);
        else if(tx.LatestUnreversed(activityId,day) is RecordEvent positive)
        {
            record=tx.InsertEvent(new(0,id,activityId,now.ToUnixTimeMilliseconds(),day,(int)now.Offset.TotalMinutes,-1,-positive.DeltaAmountAtoms,positive.Id){DeltaUnits=-positive.DeltaUnits});
            if(positive.DeltaUnits>1)
                tx.InsertEvent(new(0,Guid.NewGuid(),activityId,now.ToUnixTimeMilliseconds(),day,(int)now.Offset.TotalMinutes,1,
                    positive.DeltaAmountAtoms-positive.DeltaAmountAtoms/positive.DeltaUnits,null){DeltaUnits=positive.DeltaUnits-1});
        }
        if(record is not null && operation=="add") record=tx.InsertEvent(record);
        if(record is not null)SaveAfterQuantity(tx,activity,day,before,events,operation=="add",now);
        var summary=Rules.Daily(activity,tx.DailyAggregates(activityId,day,day),day,day,day)[0];
        var result=new CountResult(record is null?"no_change":"changed",record,summary);
        tx.InsertReceipt(new(id,activityId,operation,result)); return result;
    });
    public IReadOnlyList<DailySummary> Daily(Guid id, DateOnly from, DateOnly to) => store.Transaction(tx=>
        Rules.Daily(Get(tx,id),tx.DailyAggregates(id,from,to),from,to,DateOnly.FromDateTime(clock.Now.DateTime)));
    public object StatisticsContext(Guid id,string background) => store.Transaction(tx=>
    {
        var activity=Get(tx,id);var days=tx.DailyAggregates(id,DateOnly.MinValue,DateOnly.MaxValue);
        var total=days.Aggregate(System.Numerics.BigInteger.Zero,(sum,day)=>sum+day.AmountAtoms);
        var today=DateOnly.FromDateTime(clock.Now.DateTime);
        return new {total_atoms=total.ToString(System.Globalization.CultureInfo.InvariantCulture),
            years=days.Select(d=>d.Date.Year).Append(activity.CreatedLocalDate.Year).Append(today.Year).Distinct().Order().ToArray(),
            colors=Rules.HeatmapColors(background,activity.Config.Visual.AccentColor)};
    });
    public DayDetail Detail(Guid id, DateOnly day) => store.Transaction(tx=>
    {
        var activity=Get(tx,id); var events=tx.DayEvents(id,day);
        var summary=Rules.Daily(activity,tx.DailyAggregates(id,day,day),day,day,DateOnly.FromDateTime(clock.Now.DateTime))[0];
        var recording=ReadRecording(tx,activity,day,events);
        return new DayDetail(summary,events,recording.Grouped?recording.Groups:Array.Empty<GroupSummary>()){Recording=recording};
    });
    public IReadOnlyDictionary<Guid,IReadOnlyList<DailySummary>> RecentMatrix(DateOnly end, int days=7) => store.Transaction(tx=>
    {
        Rules.Require(days is >=1 and <=90 && end.DayNumber>=days-1,"invalid_range","矩阵日期范围无效。");
        var start=end.AddDays(1-days); var today=DateOnly.FromDateTime(clock.Now.DateTime);
        var aggregates=tx.MatrixAggregates(start,end).ToLookup(x=>x.ActivityId);
        return tx.ListActivities(false).ToDictionary(x=>x.Id,x=>Rules.Daily(x,aggregates[x.Id].ToArray(),start,end,today));
    });
}
