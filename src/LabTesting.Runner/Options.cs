using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabTesting.Runner;

internal sealed class SuiteConfig
{
    public string Server { get; set; } = "";
    public string? ServerExecutable { get; set; }
    public string Harness { get; set; } = "";
    public string[] Tests { get; set; } = [];
    public string[] Plugins { get; set; } = [];
    public string[] Dependencies { get; set; } = [];
    public DeploymentFile[] Files { get; set; } = [];
    public Dictionary<string, string> ServerSettings { get; set; } = [];
    public string[] Assemblies { get; set; } = [];
    public string[] Collections { get; set; } = [];
    public string[] TestNames { get; set; } = [];
    public bool FrameworkTests { get; set; }
    public bool FailOnSkipped { get; set; }
    public int Port { get; set; } = 7777;
    public int TimeoutSeconds { get; set; } = 600;
    public int Tickrate { get; set; } = 60;
    public string Reports { get; set; } = "TestResults";
    public string? Work { get; set; }
    public bool Keep { get; set; }
}
internal sealed class DeploymentFile
{
    public string Source { get; set; } = "";
    // serverConfig, labapiConfig or data; destination is relative to that root.
    public string Root { get; set; } = "data";
    public string Target { get; set; } = "";
}
internal sealed record Options(SuiteConfig Suite, bool List)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static Options Parse(string[] args)
    {
        if (args.Length == 0 || (args[0] != "run" && args[0] != "list"))
            throw new ArgumentException("Usage: labtest run|list --config labtesting.json [--server DIR] [--server-executable FILE] [--port N] [--timeout N] [--fail-on-skipped] [--keep]");
        string? config = null;
        for (int i = 1; i < args.Length; i++)
            if (args[i] == "--config") config = Path.GetFullPath(args[++i]);
        var c = config == null ? new SuiteConfig() :
            JsonSerializer.Deserialize<SuiteConfig>(File.ReadAllText(config), Json)
            ?? throw new ArgumentException("Configuration vide.");
        string root = config == null ? Environment.CurrentDirectory : Path.GetDirectoryName(config)!;
        string Resolve(string p) => Path.GetFullPath(p, root);
        c.Server = c.Server.Length == 0 ? "" : Resolve(c.Server);
        c.Harness = c.Harness.Length == 0 ? "" : Resolve(c.Harness);
        c.Tests = c.Tests.Select(Resolve).ToArray();
        c.Plugins = c.Plugins.Select(Resolve).ToArray();
        c.Dependencies = c.Dependencies.Select(Resolve).ToArray();
        foreach (var f in c.Files) f.Source = Resolve(f.Source);
        c.Reports = Resolve(c.Reports);
        if (c.Work != null) c.Work = Resolve(c.Work);
        for (int i = 1; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Valeur manquante.");
            switch (args[i])
            {
                case "--config": Value(); break;
                case "--server": c.Server = Path.GetFullPath(Value()); break;
                case "--server-executable": c.ServerExecutable = Value(); break;
                case "--plugin": c.Harness = Path.GetFullPath(Value()); break;
                case "--framework-directory":
                    string framework = Path.GetFullPath(Value());
                    c.Harness = Path.Combine(framework, "LabTesting.dll");
                    string harmony = Path.Combine(framework, "0Harmony.dll");
                    if (!c.Dependencies.Contains(harmony, StringComparer.OrdinalIgnoreCase))
                        c.Dependencies = [.. c.Dependencies, harmony];
                    break;
                case "--test-assembly":
                    string assemblyPath = Path.GetFullPath(Value());
                    if (!c.Tests.Contains(assemblyPath, StringComparer.OrdinalIgnoreCase))
                        c.Tests = [.. c.Tests, assemblyPath];
                    break;
                case "--port": c.Port = int.Parse(Value()); break;
                case "--timeout": c.TimeoutSeconds = int.Parse(Value()); break;
                case "--work": c.Work = Path.GetFullPath(Value()); break;
                case "--reports": c.Reports = Path.GetFullPath(Value()); break;
                case "--assembly": c.Assemblies = [.. c.Assemblies, Value()]; break;
                case "--collection": c.Collections = [.. c.Collections, Value()]; break;
                case "--test": c.TestNames = [.. c.TestNames, Value()]; break;
                case "--framework-tests": c.FrameworkTests = true; break;
                case "--fail-on-skipped": c.FailOnSkipped = true; break;
                case "--keep": c.Keep = true; break;
                default: throw new ArgumentException("Argument inconnu: " + args[i]);
            }
        }
        if (!Directory.Exists(c.Server) || !File.Exists(c.Harness))
            throw new ArgumentException("server et harness doivent exister.");
        if (c.Tests.Length == 0 && !c.FrameworkTests)
            throw new ArgumentException("Déclarez tests ou activez explicitement frameworkTests.");
        if (c.Port is < 1 or > 65535 || c.TimeoutSeconds is < 1 or > 86400 || c.Tickrate is < 1 or > 1000)
            throw new ArgumentException("Port, timeout ou tickrate invalide.");
        return new Options(c, args[0] == "list");
    }
}
