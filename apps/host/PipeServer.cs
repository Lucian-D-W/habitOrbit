using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Pulse.Host;

internal sealed class PipeServer(string name,Func<string,Task<object>> request,Action<Exception> report,Action exit):IDisposable
{
    public const int MaxBytes=2*1024*1024;
    private readonly CancellationTokenSource stop=new();
    private readonly ConcurrentDictionary<long,Connection> connections=new();
    private Task? listener;
    private long nextId;
    private readonly object shutdownGate=new();
    private readonly ExitCoordinator shutdown=new();
    public object RequestExit()
    {
        lock(shutdownGate)
        {
            var token=shutdown.Start(connections.Values.Where(c=>c.Subscribed && c.ManagerSession is not null).Select(c=>c.ManagerSession!.Value));
            if(!shutdown.Committed)Notify(new{method="host.exit_requested",@params=new{requestId=token}});
            return new{exiting=shutdown.Committed,requestId=token};
        }
    }
    public object VoteExit(Guid token,Guid session,bool allow)
    {
        lock(shutdownGate)
        {
            var decision=shutdown.Vote(token,session,allow);
            if(decision=="stale")throw new Pulse.Core.PulseException("stale_exit","退出请求已失效，请重新操作。");
            if(decision=="cancelled")Notify(new{method="host.exit_cancelled",@params=new{requestId=token}});
            return new{exiting=decision=="committed",decision};
        }
    }
    public async Task FinishExit()
    {
        // Flush the explicit shutdown signal before ending Host; a disconnect alone is never consent.
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{method="host.exiting",@params=new{}},RpcJson.Options);
        var flushed=new List<Task>();
        foreach(var c in connections.Values.Where(c=>c.Subscribed))
        {
            var ack=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if(c.Outgoing.Writer.TryWrite(new(bytes,ack)))flushed.Add(ack.Task.WaitAsync(c.Stop.Token));
        }
        try{await Task.WhenAll(flushed).WaitAsync(TimeSpan.FromSeconds(3));}
        catch(Exception e) when(e is OperationCanceledException or TimeoutException){report(e);}
        exit();
    }
    public void Start() => listener=Listen();
    private async Task Listen()
    {
        while(!stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe=null;
            try
            {
                pipe=new(name,PipeDirection.InOut,16,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                if(connections.Count>=15){pipe.Dispose();continue;}
                var id=Interlocked.Increment(ref nextId);var connection=new Connection(pipe,stop.Token);
                connections[id]=connection;_ = Serve(id,connection);pipe=null;
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested) {break;}
            catch(Exception e) {report(e);await Task.Delay(250,stop.Token).ConfigureAwait(false);}
            finally{pipe?.Dispose();}
        }
    }
    private async Task Serve(long id,Connection c)
    {
        var writer=c.Write();
        try
        {
            var reader=new FrameReader(c.Pipe);
            while(!c.Stop.IsCancellationRequested)
            {
                var line=await reader.Read(c.Stop.Token);if(line is null)break;
                if(line.Length==0)continue;
                using var doc=JsonDocument.Parse(line);
                var response=await request(line);
                var reply=JsonSerializer.SerializeToElement(response,RpcJson.Options);
                doc.RootElement.TryGetProperty("method",out var method);
                if(method.GetString()=="state.subscribe" && reply.GetProperty("ok").GetBoolean())
                {
                    lock(shutdownGate)
                    {
                        if(doc.RootElement.TryGetProperty("params",out var parameters) &&
                            (parameters.TryGetProperty("managerSession",out var session)||parameters.TryGetProperty("manager_session",out session)) && session.TryGetGuid(out var manager))c.ManagerSession=manager;
                        c.Subscribed=true;
                    }
                }
                var acknowledged=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if(!c.Outgoing.Writer.TryWrite(new(JsonSerializer.SerializeToUtf8Bytes(response,RpcJson.Options),acknowledged)))break;
                await acknowledged.Task.WaitAsync(c.Stop.Token);
                if(method.ValueKind==JsonValueKind.String && method.GetString() is "host.exit" or "host.exit_reply" &&
                    reply.GetProperty("ok").GetBoolean() && reply.GetProperty("result").GetProperty("exiting").GetBoolean()) {await FinishExit();break;}
            }
        }
        catch(Exception e) when(e is IOException or JsonException or OperationCanceledException or ObjectDisposedException){if(e is not OperationCanceledException)report(e);}
        catch(Exception e){report(e);}
        finally
        {
            connections.TryRemove(id,out _);c.Dispose();
            lock(shutdownGate)if(c.ManagerSession is Guid manager && shutdown.Disconnect(manager))Notify(new{method="host.exit_cancelled",@params=new{}});
            try{await writer;}catch(Exception e) when(e is IOException or OperationCanceledException or ObjectDisposedException){ }
        }
    }
    public void Notify(object notification)
    {
        var frame=JsonSerializer.SerializeToUtf8Bytes(notification,RpcJson.Options);
        foreach(var c in connections.Values)if(c.Subscribed && !c.Outgoing.Writer.TryWrite(new(frame,null)))c.Dispose();
    }
    public void Dispose(){stop.Cancel();foreach(var c in connections.Values)c.Dispose();}
    private sealed record Frame(byte[] Bytes,TaskCompletionSource? Acknowledged);
    private sealed class Connection(NamedPipeServerStream pipe,CancellationToken token):IDisposable
    {
        public readonly NamedPipeServerStream Pipe=pipe;
        public readonly CancellationTokenSource Stop=CancellationTokenSource.CreateLinkedTokenSource(token);
        public readonly Channel<Frame> Outgoing=Channel.CreateBounded<Frame>(new BoundedChannelOptions(64){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
        public volatile bool Subscribed;
        public Guid? ManagerSession;
        public async Task Write()
        {
            await foreach(var frame in Outgoing.Reader.ReadAllAsync(Stop.Token))
            {
                if(frame.Bytes.Length>MaxBytes){Stop.Cancel();throw new IOException("响应超过2 MiB，需要缩小查询范围。");}
                try{await Pipe.WriteAsync(frame.Bytes,Stop.Token);await Pipe.WriteAsync(new byte[]{10},Stop.Token);await Pipe.FlushAsync(Stop.Token);frame.Acknowledged?.TrySetResult();}
                catch{Stop.Cancel();throw;}
            }
        }
        public void Dispose(){Stop.Cancel();Outgoing.Writer.TryComplete();Pipe.Dispose();}
    }
    internal sealed class FrameReader(Stream stream)
    {
        private readonly byte[] buffer=new byte[8192];private int offset,count;
        private static readonly UTF8Encoding Utf8=new(false,true);
        public async Task<string?> Read(CancellationToken token)
        {
            using var frame=new MemoryStream();
            while(true)
            {
                if(offset==count){count=await stream.ReadAsync(buffer,token);offset=0;if(count==0){if(frame.Length!=0)throw new IOException("JSON帧未结束。");return null;}}
                var end=Array.IndexOf(buffer,(byte)10,offset,count-offset);var take=(end<0?count:end)-offset;
                if(frame.Length+take>MaxBytes)throw new IOException("请求超过2 MiB。");
                frame.Write(buffer,offset,take);offset+=take;
                if(end>=0){offset++;return Utf8.GetString(frame.ToArray()).TrimEnd('\r');}
            }
        }
    }
}
