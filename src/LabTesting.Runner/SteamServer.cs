using System.Text.RegularExpressions;

namespace LabTesting.Runner;

internal static class SteamServer
{
    private const int AppId = 996560;

    internal static string? Locate() => Locate(Roots());

    internal static string? Locate(IEnumerable<string> roots)
    {
        foreach (string library in Libraries(roots))
        {
            string manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
            if (!File.Exists(manifest)) continue;
            Match install = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\"");
            if (!install.Success) continue;
            string candidate = Path.Combine(library, "steamapps", "common", install.Groups[1].Value);
            if (File.Exists(Path.Combine(candidate, "SCPSL_Data", "Managed", "Assembly-CSharp.dll")))
                return candidate;
        }
        return null;
    }

    internal static IEnumerable<string> Libraries(IEnumerable<string> roots)
    {
        foreach (string root in roots)
        {
            yield return root;
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                yield return m.Groups[1].Value.Replace("\\\\", "\\");
        }
    }

    private static IEnumerable<string> Roots()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return @"C:\Program Files (x86)\Steam";
            yield break;
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
    }
}
