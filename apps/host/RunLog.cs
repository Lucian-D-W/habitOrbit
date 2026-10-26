namespace Pulse.Host;

internal sealed class RunLog(string directory)
{
    private readonly object gate=new();
    public void Write(Exception error)
    {
        lock(gate)
        {
            try
            {
                Directory.CreateDirectory(directory);var path=Path.Combine(directory,"host.log");
                if(File.Exists(path) && new FileInfo(path).Length>512*1024)File.Move(path,Path.Combine(directory,"host.previous.log"),true);
                File.AppendAllText(path,$"{DateTimeOffset.Now:O} {error}\n");
            }
            catch(IOException){Console.Error.WriteLine(error.Message);}
        }
    }
}
