using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace LabTesting.Runner;

internal sealed record Layout(string Installation, string Config, string Executable);
internal static class Deployment
{
    internal static string Under(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Contains(':') || relative.Split('/', '\\').Any(x => x is ".." or "." or ""))
            throw new ArgumentException("Chemin relatif invalide: " + relative);
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Chemin hors du dossier: " + relative);
        return path;
    }

    internal static Layout Prepare(Options o, string work, string reports, CancellationToken token)
    {
        var c = o.Suite;
        string install = Path.Combine(work, "server");
        string source = Path.GetFullPath(c.Server);
        if ((work + Path.DirectorySeparatorChar).StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Le travail doit être en dehors du serveur source.");
        Directory.CreateDirectory(install);
        // Only shipped runtime entries: no inherited AppData, policy or plugin directories.
        foreach (string name in new[] { "SCPSL_Data", "MonoBleedingEdge", "ConfigTemplates", "Translations", "D3D12", "linux64" })
        {
            string dir = Path.Combine(source, name);
            if (Directory.Exists(dir)) CopyTree(dir, Path.Combine(install, name), token);
        }
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string name = Path.GetFileName(file);
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                name.Contains(".so.", StringComparison.OrdinalIgnoreCase) ||
                name is "SCPSL.exe" or "SCPSL.x86_64" or "SCPSL" or "steam_appid.txt")
                Copy(file, Path.Combine(install, name));
        }
        string executable = c.ServerExecutable ?? (OperatingSystem.IsWindows() ? "SCPSL.exe" :
            File.Exists(Path.Combine(source, "SCPSL.x86_64")) ? "SCPSL.x86_64" : "SCPSL");
        string original = Path.GetFullPath(executable, source);
        if (!File.Exists(original)) throw new FileNotFoundException("Exécutable serveur absent.", original);
        string exe = Path.Combine(install, Path.GetFileName(original));
        if (File.Exists(exe) && !original.Equals(Path.Combine(source, Path.GetFileName(original)), StringComparison.OrdinalIgnoreCase))
            File.Delete(exe);
        if (!File.Exists(exe)) Copy(original, exe);
        string config = Path.Combine(work, "config");
        string labapi = Path.Combine(install, "AppData", "SCP Secret Laboratory", "LabAPI");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(install, "hoster_policy.txt"), "gamedir_for_configs: true\n");
        File.WriteAllText(Path.Combine(config, "LABTESTING_ENABLED"), "labtest\n");
        File.WriteAllText(Path.Combine(config, "config_gameplay.txt"),
            "online_mode: false\nrestart_after_rounds: 0\nserver_tickrate: " + c.Tickrate +
            "\nserver_name: LabTesting\nidle_mode_enabled: false\nenable_fast_round_restart: true\n");
        foreach (var setting in c.ServerSettings)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(setting.Key, "^[a-z_]+$") ||
                setting.Value.IndexOfAny(['\r', '\n']) >= 0 ||
                new[] { "online_mode", "restart_after_rounds", "server_tickrate", "idle_mode_enabled", "enable_fast_round_restart", "server_name" }.Contains(setting.Key))
                throw new ArgumentException("Paramètre serveur réservé ou invalide: " + setting.Key);
            File.AppendAllText(Path.Combine(config, "config_gameplay.txt"), setting.Key + ": " + setting.Value + "\n");
        }
        string plugins = Path.Combine(labapi, "plugins", c.Port.ToString());
        string dependencies = Path.Combine(labapi, "dependencies", c.Port.ToString());
        string tests = Path.Combine(work, "tests");
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        targets.Add(Path.Combine(config, "labtesting-suite.xml"));
        targets.Add(Path.Combine(config, "config_sharing.txt"));
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifest = new List<object>();
        string Deploy(string file, string target, bool assembly)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Fichier à déployer absent.", file);
            if (!targets.Add(target) || File.Exists(target)) throw new IOException("Collision: " + target);
            string? identity = assembly ? AssemblyName.GetAssemblyName(file).FullName : null;
            if (assembly && !identities.Add(AssemblyName.GetAssemblyName(file).Name!))
                throw new IOException("Assembly ambiguë: " + identity);
            if (assembly && Directory.EnumerateFiles(Path.Combine(install, "SCPSL_Data", "Managed"), "*.dll")
                    .Any(p => Path.GetFileName(p).Equals(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Ne pas redéployer une assembly du serveur: " + file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Copy(file, target);
            manifest.Add(new { file = Path.GetFileName(file), identity, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) });
            return target;
        }
        Deploy(c.Harness, Path.Combine(plugins, Path.GetFileName(c.Harness)), true);
        foreach (var file in c.Plugins) Deploy(file, Path.Combine(plugins, Path.GetFileName(file)), true);
        foreach (var file in c.Dependencies) Deploy(file, Path.Combine(dependencies, Path.GetFileName(file)), true);
        var testPaths = c.Tests.Select(file => Deploy(file, Path.Combine(tests, Path.GetFileName(file)), true)).ToArray();
        foreach (var file in c.Files)
        {
            string root = file.Root switch
            {
                "serverConfig" => config,
                "labapiConfig" => Path.Combine(labapi, "configs", c.Port.ToString()),
                "data" => Path.Combine(install, "test-data"),
                _ => throw new ArgumentException("Root invalide: " + file.Root)
            };
            if (file.Target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Déclarer les DLL dans tests, plugins ou dependencies.");
            Deploy(file.Source, Under(root, file.Target), false);
        }
        var request = new XElement("suite",
            new XAttribute("list", o.List), new XAttribute("frameworkTests", c.FrameworkTests),
            new XAttribute("reports", reports),
            new XElement("tests", testPaths.Select(p => new XElement("path", p))),
            new XElement("assemblies", c.Assemblies.Select(p => new XElement("name", p))),
            new XElement("collections", c.Collections.Select(p => new XElement("name", p))),
            new XElement("testNames", c.TestNames.Select(p => new XElement("name", p))),
            new XElement("plugins", c.Plugins.Select(p => new XElement("name", AssemblyName.GetAssemblyName(p).Name))));
        request.Save(Path.Combine(config, "labtesting-suite.xml"));
        File.WriteAllText(Path.Combine(reports, "deployment.json"), JsonSerializer.Serialize(manifest, Options.Json));
        return new Layout(install, config, exe);
    }

    private static void Copy(string source, string target)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Lien symbolique interdit: " + source);
        File.Copy(source, target, false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(source));
    }
    private static void CopyTree(string source, string target, CancellationToken token)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Lien symbolique interdit: " + source);
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source)) { token.ThrowIfCancellationRequested(); Copy(file, Path.Combine(target, Path.GetFileName(file))); }
        foreach (string dir in Directory.EnumerateDirectories(source)) CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)), token);
    }
    internal static void DeleteOwned(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path);
            return;
        }
        // Keep the recovery marker until all other entries were removed successfully.
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos().OrderBy(e => e.Name == ".labtesting-owned"))
        {
            if ((entry.Attributes & FileAttributes.Directory) != 0 && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                DeleteOwned(entry.FullName);
            else
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                entry.Delete();
            }
        }
        Directory.Delete(path);
    }
}
