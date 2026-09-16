using System.Diagnostics;
using System.Xml.Linq;

namespace LabTesting.Runner;

/// <summary>
/// Executable check of the only non-trivial piece of runner logic: reading the JSONL
/// and deriving the exit code. Runs without a server - `labtest --selftest`.
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        int failures = 0;

        // A passing test, with a message holding quotes, a backslash and a newline.
        const string passed =
            """
            {"id":"A.b","collection":"C","outcome":"passed","durationTicks":42,"isolation":"Dummies","escalated":false,"uptimeRound":3,"failures":[],"swallowed":[]}
            """;

        Check(ref failures, "id", "A.b", Verdict.Field(passed, "id"));
        Check(ref failures, "outcome", "passed", Verdict.Field(passed, "outcome"));
        Check(ref failures, "durationTicks", 42, Verdict.Number(passed, "durationTicks"));
        Check(ref failures, "escalated", false, Verdict.Bool(passed, "escalated"));
        Check(ref failures, "empty failures", 0, Verdict.Objects(passed, "failures").Count);

        const string failed =
            """
            {"id":"A.c","collection":"C","outcome":"failed","durationTicks":7,"isolation":"Round","escalated":true,"uptimeRound":4,"failures":[{"assert":"Eventually","expected":"Xp == 25","observed":"0","afterTicks":30,"because":"it says \"no\" \\ never\nnext"}],"swallowed":[{"source":"LabApi","message":"boom"}]}
            """;

        Check(ref failures, "escalated true", true, Verdict.Bool(failed, "escalated"));

        List<string> fails = Verdict.Objects(failed, "failures");
        Check(ref failures, "1 failure", 1, fails.Count);
        if (fails.Count == 1)
        {
            Check(ref failures, "assert", "Eventually", Verdict.Field(fails[0], "assert"));
            Check(ref failures, "afterTicks", 30, Verdict.Number(fails[0], "afterTicks"));
            Check(ref failures, "unescaping", "it says \"no\" \\ never\nnext", Verdict.Field(fails[0], "because"));
        }

        List<string> swallowed = Verdict.Objects(failed, "swallowed");
        Check(ref failures, "1 swallowed", 1, swallowed.Count);
        if (swallowed.Count == 1)
            Check(ref failures, "source", "LabApi", Verdict.Field(swallowed[0], "source"));

        // A brace inside a string must not break how the array is split.
        const string tricky =
            """
            {"id":"A.d","outcome":"failed","durationTicks":1,"failures":[{"assert":"X","expected":"{ not an object }","observed":"]","afterTicks":0}],"swallowed":[]}
            """;

        List<string> trickyFails = Verdict.Objects(tricky, "failures");
        Check(ref failures, "brace inside a string", 1, trickyFails.Count);
        if (trickyFails.Count == 1)
            Check(ref failures, "literal expected", "{ not an object }", Verdict.Field(trickyFails[0], "expected"));

        // The key must not be confused with text that merely contains it.
        const string decoy =
            """
            {"id":"\"outcome\":\"passed\"","outcome":"failed","durationTicks":0,"failures":[],"swallowed":[]}
            """;

        Check(ref failures, "decoy key", "failed", Verdict.Field(decoy, "outcome"));

        // Deriving the verdict.
        Verdict all = Verdict.Parse([
            """{"kind":"plan","list":false,"tests":[{"id":"A.b","collection":"C"},{"id":"A.c","collection":"C"}]}""",
            passed,
            failed,
            """{"kind":"summary","passed":1,"failed":1,"skipped":0,"errors":0}"""
        ]);

        Check(ref failures, "summary present", true, all.HasSummary);
        Check(ref failures, "passed", 1, all.Passed);
        Check(ref failures, "failed", 1, all.Failed);
        Check(ref failures, "rendered lines", 2, all.Lines.Count);
        Check(ref failures, "no protocol error", 0, all.Problems.Count);
        Check(ref failures, "assertion failure exit code", 1, all.ExitCode(false));
        Check(ref failures, "summary line", "1 passed, 1 failed, 0 skipped, 0 errors", all.Summary);

        const string plan = """{"kind":"plan","list":false,"tests":[{"id":"A.b","collection":"C"}]}""";
        const string summary = """{"kind":"summary","passed":1,"failed":0,"skipped":0,"errors":0}""";
        Verdict valid = Verdict.Parse([plan, passed, summary]);
        Check(ref failures, "valid suite", 0, valid.ExitCode(false));
        foreach (string[] corrupt in new[]
        {
            new[] { passed, summary },
            new[] { plan, summary },
            new[] { plan, passed, passed, summary },
            new[] { plan, passed, summary, summary },
            new[] { plan, passed, summary, passed },
            new[] { plan, passed, "{" },
            new[] { plan, passed, """{"kind":"summary","passed":0,"failed":0,"skipped":0,"errors":0}""" },
            new[] { plan, passed, """{"kind":"summary","passed":1,"failed":0,"skipped":0,"errors":0,"harnessError":"teardown"}""" },
            new[] { """{"kind":"plan","list":false,"tests":[]}""", """{"kind":"summary","passed":0,"failed":0,"skipped":0,"errors":0}""" }
        })
            Check(ref failures, "corrupt verdict rejected", 2, Verdict.Parse(corrupt).ExitCode(false));
        Verdict skip = Verdict.Parse([
            plan,
            """{"id":"A.b","collection":"C","outcome":"skipped","skip":"raison"}""",
            """{"kind":"summary","passed":0,"failed":0,"skipped":1,"errors":0}"""
        ]);
        Check(ref failures, "skip accepted", 0, skip.ExitCode(false));
        Check(ref failures, "skip rejected", 1, skip.ExitCode(true));
        Verdict list = Verdict.Parse([
            plan.Replace("false", "true"),
            """{"kind":"summary","passed":0,"failed":0,"skipped":0,"errors":0}"""
        ], true);
        Check(ref failures, "list", 0, list.ExitCode(false));
        Check(ref failures, "list summary line", "1 discovered", list.Summary);
        foreach (string path in new[] { "../escape", "a/../../escape", "/absolute", "C:\\escape", "a\\..\\escape" })
        {
            try { Deployment.Under(Path.GetTempPath(), path); Check(ref failures, "traversal rejected", true, false); }
            catch (ArgumentException) { }
        }
        string reports = Path.Combine(Path.GetTempPath(), "labtest-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reports);
        try
        {
            Verdict.Parse([plan]).WriteReports(reports);
            XDocument xml = System.Xml.Linq.XDocument.Load(Path.Combine(reports, "junit.xml"));
            Check(ref failures, "partial report in error", true, xml.Descendants("error").Any());
        }
        finally { Deployment.DeleteOwned(reports); }
        CheckProcessTree(ref failures);
        CheckSteamServer(ref failures);

        Verdict truncated = Verdict.Parse([passed]);
        Check(ref failures, "no summary", false, truncated.HasSummary);
        // A run that produced nothing must not read as a clean "0 failed" in the log.
        Check(ref failures, "problems surface in the summary line",
            "0 passed, 0 failed, 0 skipped, 0 errors, 3 infrastructure problem(s)", truncated.Summary);

        Console.WriteLine(failures == 0
            ? "[labtest] selftest: every check passes."
            : "[labtest] selftest: " + failures + " failure(s).");

        return failures == 0 ? 0 : 1;
    }

    private static void Check<T>(ref int failures, string what, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
            return;

        failures++;
        Console.Error.WriteLine("  FAILED " + what + ": expected <" + expected + ">, actual <" + actual + ">");
    }

    internal static System.Diagnostics.ProcessStartInfo SelfStart(string argument, bool session = false)
    {
        string executable = Environment.ProcessPath!;
        ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
        if (session && OperatingSystem.IsLinux())
        {
            start.FileName = "/usr/bin/setsid";
            start.ArgumentList.Add(executable);
        }
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet")
            start.ArgumentList.Add(typeof(SelfTest).Assembly.Location);
        start.ArgumentList.Add(argument);
        return start;
    }

    private static void CheckSteamServer(ref int failures)
    {
        string root = Path.Combine(Path.GetTempPath(), "labtest-selftest-steam-" + Guid.NewGuid().ToString("N"));
        string library = Path.Combine(root, "library");
        string common = Path.Combine(library, "steamapps", "common", "SCP Secret Laboratory Dedicated Server");
        try
        {
            Directory.CreateDirectory(Path.Combine(common, "SCPSL_Data", "Managed"));
            File.WriteAllText(Path.Combine(common, "SCPSL_Data", "Managed", "Assembly-CSharp.dll"), "");
            File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_996560.acf"),
                "\"AppState\"\n{\n\t\"installdir\"\t\"SCP Secret Laboratory Dedicated Server\"\n}\n");
            Check(ref failures, "steam server found via appmanifest", common, SteamServer.Locate([library]));

            string indirect = Path.Combine(root, "indirect");
            Directory.CreateDirectory(Path.Combine(indirect, "steamapps"));
            File.WriteAllText(Path.Combine(indirect, "steamapps", "libraryfolders.vdf"),
                "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\"" + library.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
            Check(ref failures, "steam server found via libraryfolders.vdf", common, SteamServer.Locate([indirect]));

            Check(ref failures, "no server without a matching manifest", null, SteamServer.Locate([root]));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckProcessTree(ref int failures)
    {
        ProcessStartInfo start = SelfStart("--selftest-parent", session: true);
        start.RedirectStandardOutput = true;
        using Process parent = System.Diagnostics.Process.Start(start)!;
        using ProcessContainment containment = new ProcessContainment();
        System.Diagnostics.Process? child = null;
        try
        {
            containment.Attach(parent);
            int pid = int.Parse(parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()!);
            child = System.Diagnostics.Process.GetProcessById(pid);
            containment.Dispose();
            Check(ref failures, "parent process stopped", true, parent.WaitForExit(5000));
            // A dead orphan can remain a zombie briefly under WSL's init.
            bool zombie = OperatingSystem.IsLinux() && File.Exists("/proc/" + pid + "/status") &&
                File.ReadLines("/proc/" + pid + "/status").Any(x => x.StartsWith("State:") && x.Contains('Z'));
            Check(ref failures, "child process stopped", true, zombie || child.WaitForExit(5000));
        }
        catch (Exception e)
        {
            failures++;
            Console.Error.WriteLine("FAILED to stop the process tree: " + e.Message);
        }
        finally
        {
            if (!parent.HasExited) parent.Kill(true);
            if (child != null)
            {
                if (!child.HasExited) child.Kill(true);
                child.Dispose();
            }
        }
    }
}
