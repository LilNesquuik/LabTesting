using CommandLine;
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
    public string[] Traits { get; set; } = [];
    public string[] ExcludeTraits { get; set; } = [];
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

internal abstract class SuiteArguments
{
    [Option("config", HelpText = "Path to labtesting.json; auto-discovered by walking up from the current directory when omitted")]
    public string? Config { get; set; }

    [Option("server", HelpText = "Server installation directory")]
    public string? Server { get; set; }

    [Option("server-executable", HelpText = "Explicit native binary, relative to the server or absolute")]
    public string? ServerExecutable { get; set; }

    [Option("framework-directory", Hidden = true)]
    public string? FrameworkDirectory { get; set; }

    [Option("test-assembly", Hidden = true)]
    public string? TestAssembly { get; set; }

    [Option("port", HelpText = "UDP/TCP, 1..65535")]
    public int? Port { get; set; }

    [Option("timeout", HelpText = "Seconds, 1..86400")]
    public int? Timeout { get; set; }

    [Option("work", HelpText = "Parent of the temporary copies")]
    public string? Work { get; set; }

    [Option("reports", HelpText = "Parent of the persistent reports")]
    public string? Reports { get; set; }

    [Option("assembly", HelpText = "Exact filter, one or more names")]
    public IEnumerable<string> Assemblies { get; set; } = [];

    [Option("collection", HelpText = "Exact filter, one or more names")]
    public IEnumerable<string> Collections { get; set; } = [];

    [Option("test", HelpText = "Exact filter, one or more identifiers")]
    public IEnumerable<string> Tests { get; set; } = [];

    [Option("trait", HelpText = "Exact filter, one or more name=value tags")]
    public IEnumerable<string> Traits { get; set; } = [];

    [Option("exclude-trait", HelpText = "Drops tests carrying any of these name=value tags")]
    public IEnumerable<string> ExcludeTraits { get; set; } = [];

    [Option("framework-tests", HelpText = "Adds the framework's own tests")]
    public bool FrameworkTests { get; set; }

    [Option("fail-on-skipped")]
    public bool FailOnSkipped { get; set; }

    [Option("keep", HelpText = "Keeps the copy after the run")]
    public bool Keep { get; set; }
}

[Verb("run", HelpText = "Run the suite.")]
internal sealed class RunVerb : SuiteArguments;

[Verb("list", HelpText = "Discover the suite without running it.")]
internal sealed class ListVerb : SuiteArguments;

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
        ParserResult<object> result = new Parser(settings => settings.HelpWriter = null)
            .ParseArguments<RunVerb, ListVerb>(args);

        return result.MapResult(
            (RunVerb run) => FromArguments(run, list: false),
            (ListVerb list) => FromArguments(list, list: true),
            _ => throw new ArgumentException(CommandLine.Text.HelpText.AutoBuild(result).ToString()));
    }

    private static Options FromArguments(SuiteArguments a, bool list)
    {
        string configPath = Path.GetFullPath(string.IsNullOrEmpty(a.Config) ? DiscoverConfig() : a.Config);
        SuiteConfig c = JsonSerializer.Deserialize<SuiteConfig>(File.ReadAllText(configPath), Json)
            ?? throw new ArgumentException("Empty configuration.");
        string root = Path.GetDirectoryName(configPath)!;
        string Resolve(string p) => Path.GetFullPath(p, root);

        c.Server = c.Server.Length == 0 ? "" : Resolve(c.Server);
        c.Harness = c.Harness.Length == 0 ? "" : Resolve(c.Harness);
        c.Tests = [.. c.Tests.Select(Resolve)];
        c.Plugins = [.. c.Plugins.Select(Resolve)];
        c.Dependencies = [.. c.Dependencies.Select(Resolve)];

        foreach (DeploymentFile f in c.Files)
            f.Source = Resolve(f.Source);
        c.Reports = Resolve(c.Reports);
        if (c.Work != null)
            c.Work = Resolve(c.Work);

        if (a.Server != null)
            c.Server = Path.GetFullPath(a.Server);
        if (a.ServerExecutable != null)
            c.ServerExecutable = a.ServerExecutable;

        if (a.FrameworkDirectory != null)
        {
            string framework = Path.GetFullPath(a.FrameworkDirectory);
            if (c.Harness.Length > 0)
                Console.Error.WriteLine("[labtest] harness is ignored: the NuGet targets supply it. Remove it from the configuration.");
            c.Harness = Path.Combine(framework, "LabTesting.dll");

            string harmony = Path.Combine(framework, "0Harmony.dll");
            if (!c.Dependencies.Contains(harmony, StringComparer.OrdinalIgnoreCase))
                c.Dependencies = [.. c.Dependencies, harmony];
        }

        if (a.TestAssembly != null)
        {
            string assemblyPath = Path.GetFullPath(a.TestAssembly);
            if (!c.Tests.Contains(assemblyPath, StringComparer.OrdinalIgnoreCase))
                c.Tests = [.. c.Tests, assemblyPath];
        }

        if (a.Port is { } port)
            c.Port = port;

        if (a.Timeout is { } timeout)
            c.TimeoutSeconds = timeout;

        if (a.Work != null)
            c.Work = Path.GetFullPath(a.Work);

        if (a.Reports != null)
            c.Reports = Path.GetFullPath(a.Reports);

        c.Assemblies = [.. c.Assemblies, .. a.Assemblies];
        c.Collections = [.. c.Collections, .. a.Collections];
        c.TestNames = [.. c.TestNames, .. a.Tests];
        c.Traits = [.. c.Traits, .. a.Traits];
        c.ExcludeTraits = [.. c.ExcludeTraits, .. a.ExcludeTraits];

        if (a.FrameworkTests)
            c.FrameworkTests = true;

        if (a.FailOnSkipped)
            c.FailOnSkipped = true;

        if (a.Keep)
            c.Keep = true;

        if (c.Server.Length == 0)
            c.Server = SteamServer.Locate() ?? "";

        if (c.Server.Length == 0)
            throw new ArgumentException("No server given and no local Steam install found. Install SCP Secret Laboratory Dedicated Server, or pass --server (CI always passes it explicitly).");

        if (!Directory.Exists(c.Server) || !File.Exists(c.Harness))
            throw new ArgumentException("server and harness must both exist.");

        if (c.Tests.Length == 0 && !c.FrameworkTests)
            throw new ArgumentException("Declare tests or enable frameworkTests explicitly.");

        foreach (string trait in c.Traits.Concat(c.ExcludeTraits))
            if (!trait.Contains('=')) throw new ArgumentException("Invalid trait, expected name=value: " + trait);

        if (c.Port is < 1 or > 65535 || c.TimeoutSeconds is < 1 or > 86400 || c.Tickrate is < 1 or > 1000)
            throw new ArgumentException("Invalid port, timeout or tickrate.");

        return new Options(c, list);
    }

    private static string DiscoverConfig()
    {
        for (string? dir = Environment.CurrentDirectory; dir != null; dir = Directory.GetParent(dir)?.FullName)
        {
            string candidate = Path.Combine(dir, "labtesting.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new ArgumentException("No labtesting.json found above the current directory. Pass --config, or run from inside the project.");
    }
}
