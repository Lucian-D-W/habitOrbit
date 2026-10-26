namespace Pulse.Host;

// Consent is tied to the current request and connected manager sessions.
internal sealed class ExitCoordinator
{
    private readonly HashSet<Guid> waiting=new();
    public Guid? RequestId {get;private set;}
    public bool Committed {get;private set;}
    public Guid Start(IEnumerable<Guid> managers)
    {
        if(RequestId is Guid current)return current;
        RequestId=Guid.NewGuid();waiting.UnionWith(managers);Committed=waiting.Count==0;
        return RequestId.Value;
    }
    public string Vote(Guid requestId,Guid session,bool allow)
    {
        if(RequestId!=requestId || Committed || !waiting.Contains(session))return "stale";
        if(!allow){Cancel();return "cancelled";}
        waiting.Remove(session);Committed=waiting.Count==0;return Committed?"committed":"waiting";
    }
    public bool Disconnect(Guid session)
    {
        if(RequestId is null || Committed || !waiting.Contains(session))return false;
        Cancel();return true;
    }
    private void Cancel(){RequestId=null;waiting.Clear();Committed=false;}
}
