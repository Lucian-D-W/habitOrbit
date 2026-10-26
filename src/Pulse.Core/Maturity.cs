namespace Pulse.Core;

public readonly record struct MaturityState(double Value, double TypicalInterval);

public static class Maturity
{
    // Completed intervals only: a current pause never stretches its own grace period.
    public static MaturityState Evaluate(IEnumerable<DayAggregate> history, DateOnly today)
    {
        var intervals=new Queue<int>(); double value=0,tau=1; DateOnly? last=null;
        foreach(var date in history.Where(x=>x.Date<=today && x.RecordedUnits>0).Select(x=>x.Date).Distinct().Order())
        {
            if(last is DateOnly previous)
            {
                var gap=date.DayNumber-previous.DayNumber;var broken=gap>=4*tau;
                if(broken)value=0;
                // The first closed interval seeds an otherwise unknown rhythm.
                if(intervals.Count==0 || !broken)
                {
                    intervals.Enqueue(gap);if(intervals.Count>12)intervals.Dequeue();
                    var sorted=intervals.Order().ToArray();var n=sorted.Length;
                    tau=n%2==1?sorted[n/2]:(sorted[n/2-1]+sorted[n/2])/2d;
                }
            }
            value+=.05*(1-value);last=date;
        }
        if(last is DateOnly recent && today.DayNumber-recent.DayNumber>=4*tau)value=0;
        return new(value,tau);
    }
}
