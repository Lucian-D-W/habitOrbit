using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pulse.Desktop;
using Pulse.Rendering;

namespace Pulse.Host;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.SequenceEqual(new[] { "--run" }))
            { using var runtime = new HostRuntime(ProcessEnvironment.DataDirectory()); return runtime.Run(); }
            if (args.Length == 2 && args[0] == "--data-dir")
            { using var runtime = new HostRuntime(args[1]); return runtime.Run(); }
            if (args.Length == 2 && args[0] == "--headless")
            { using var runtime = new HostRuntime(args[1],hidden:true); return runtime.Run(); }
            if (args.Length == 2 && args[0] == "--host-check") return HostChecks.Run(args[1]);
            if (args.Length == 2 && args[0] == "--host-preview") return HostPreview.Run(args[1]);
            if (args.SequenceEqual(new[] { "--orb-dev" })) return OrbDevelopment.Run();
            if (args.Length == 2 && args[0] == "--orb-check") return OrbDevelopment.Check(args[1]);
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        if (!args.SequenceEqual(new[] { "--diagnostics" }))
        {
            Console.WriteLine("Pulse Host：--diagnostics，--orb-dev，--orb-check <输出目录>。开发反馈尚未持久化。");
            return 0;
        }
        try
        {
            using var database = new SqliteConnection("Data Source=:memory:");
            database.Open();
            using var query = database.CreateCommand();
            query.CommandText = "SELECT sqlite_version()";
            var sqlite = query.ExecuteScalar();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                packaged = ProcessEnvironment.IsPackaged(),
                dataDirectory = ProcessEnvironment.DataDirectory(),
                physicalLocalAppData = ProcessEnvironment.PhysicalDirectory(ProcessEnvironment.LocalAppData()),
                sqlite,
                graphics = GraphicsReadiness.Check(),
                userDataWritten = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
