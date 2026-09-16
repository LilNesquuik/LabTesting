using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CommandLine;

namespace LabTesting.Runner;

[Verb("init", HelpText = "Scaffold labtesting.json and a GitHub Actions workflow for this project.")]
internal sealed class InitVerb
{
    [Option("test-project", HelpText = "Path to the net48 test .csproj; auto-detected when there is exactly one")]
    public string? TestProject { get; set; }
}

internal static class Init
{
    internal static int Run(string[] args)
    {
        ParserResult<InitVerb> result = new Parser(settings => settings.HelpWriter = null)
            .ParseArguments<InitVerb>(args);

        return result.MapResult(Run,
            _ => throw new ArgumentException(CommandLine.Text.HelpText.AutoBuild(result).ToString()));
    }

    private static int Run(InitVerb a)
    {
        string testProject = a.TestProject != null ? Path.GetFullPath(a.TestProject) : DiscoverTestProject();
        if (!File.Exists(testProject))
            throw new ArgumentException("Test project not found: " + testProject);

        string root = FindRepoRoot(testProject);
        string configPath = Path.Combine(root, "labtesting.json");
        string workflowPath = Path.Combine(root, ".github", "workflows", "tests.yml");
        List<string> existing = [.. new[] { configPath, workflowPath }.Where(File.Exists)];
        if (existing.Count > 0)
            throw new ArgumentException("Already exists, not overwriting: " + string.Join(", ", existing));

        List<string> plugins = [.. ReadProjectReferences(testProject).Select(dll => Relative(root, dll))];
        WriteConfig(configPath, plugins);
        WriteWorkflow(workflowPath, Relative(root, testProject));

        Console.WriteLine("[labtest] wrote " + Relative(root, configPath));
        Console.WriteLine("[labtest] wrote " + Relative(root, workflowPath));
        Console.WriteLine("[labtest] move libraries without a Plugin class from \"plugins\" to \"dependencies\" in labtesting.json.");
        return 0;
    }

    private static string FindRepoRoot(string testProject)
    {
        for (string? dir = Path.GetDirectoryName(testProject); dir != null; dir = Directory.GetParent(dir)?.FullName)
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                return dir;

        return Environment.CurrentDirectory;
    }

    private static string DiscoverTestProject()
    {
        List<string> candidates = [.. Directory
            .EnumerateFiles(Environment.CurrentDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(IsLabTestingProject)];

        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count == 0)
            throw new ArgumentException(
                "No net48 project referencing the LabTesting package found under the current directory. " +
                "Add the PackageReference first, or pass --test-project.");
        throw new ArgumentException("Multiple candidates found, pass --test-project:\n" + string.Join("\n", candidates));
    }

    private static bool IsLabTestingProject(string csproj)
    {
        string xml = File.ReadAllText(csproj);
        return Regex.IsMatch(xml, @"<TargetFrameworks?>[^<]*\bnet48\b") &&
               Regex.IsMatch(xml, "<PackageReference\\s+Include=\"LabTesting\"");
    }

    private static List<string> ReadProjectReferences(string testProject)
    {
        string testDir = Path.GetDirectoryName(testProject)!;
        List<string> dlls = [];

        foreach (XElement element in XDocument.Load(testProject).Descendants("ProjectReference"))
        {
            string? include = (string?)element.Attribute("Include");
            if (include == null) continue;

            string referenced = Path.GetFullPath(include.Replace('\\', Path.DirectorySeparatorChar), testDir);
            string assemblyName = (string?)XDocument.Load(referenced).Descendants("AssemblyName").FirstOrDefault()
                ?? Path.GetFileNameWithoutExtension(referenced);
            dlls.Add(Path.Combine(Path.GetDirectoryName(referenced)!, "bin", "Release", "net48", assemblyName + ".dll"));
        }

        return dlls;
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static void WriteConfig(string path, List<string> plugins)
    {
        string json = JsonSerializer.Serialize(new { plugins, dependencies = Array.Empty<string>() }, Options.Json);
        File.WriteAllText(path, json);
    }

    private static void WriteWorkflow(string path, string testProjectRelative)
    {
        using Stream stream = typeof(Init).Assembly.GetManifestResourceStream("nuget-tests.yml")!;
        using StreamReader reader = new StreamReader(stream);
        string template = reader.ReadToEnd().Replace("tests/MonPlugin.Tests/MonPlugin.Tests.csproj", testProjectRelative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, template);
    }
}
