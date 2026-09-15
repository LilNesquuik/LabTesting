namespace LabTesting.Runner;

/// <summary>
/// Vérification exécutable du seul morceau de logique non trivial du runner : la lecture du JSONL
/// et la dérivation du code de sortie. Tourne sans serveur — `labtest --selftest`.
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        int failures = 0;

        // Un test passé, avec un message contenant guillemets, antislash et saut de ligne.
        const string passed =
            """
            {"id":"A.b","collection":"C","outcome":"passed","durationTicks":42,"isolation":"Dummies","escalated":false,"uptimeRound":3,"failures":[],"swallowed":[]}
            """;

        Check(ref failures, "id", "A.b", Verdict.Field(passed, "id"));
        Check(ref failures, "outcome", "passed", Verdict.Field(passed, "outcome"));
        Check(ref failures, "durationTicks", 42, Verdict.Number(passed, "durationTicks"));
        Check(ref failures, "escalated", false, Verdict.Bool(passed, "escalated"));
        Check(ref failures, "failures vides", 0, Verdict.Objects(passed, "failures").Count);

        const string failed =
            """
            {"id":"A.c","collection":"C","outcome":"failed","durationTicks":7,"isolation":"Round","escalated":true,"uptimeRound":4,"failures":[{"assert":"Eventually","expected":"Xp == 25","observed":"0","afterTicks":30,"because":"il dit \"non\" \\ jamais\nsuite"}],"swallowed":[{"source":"LabApi","message":"boom"}]}
            """;

        Check(ref failures, "escalated vrai", true, Verdict.Bool(failed, "escalated"));

        List<string> fails = Verdict.Objects(failed, "failures");
        Check(ref failures, "1 échec", 1, fails.Count);
        if (fails.Count == 1)
        {
            Check(ref failures, "assert", "Eventually", Verdict.Field(fails[0], "assert"));
            Check(ref failures, "afterTicks", 30, Verdict.Number(fails[0], "afterTicks"));
            Check(ref failures, "déséchappement", "il dit \"non\" \\ jamais\nsuite", Verdict.Field(fails[0], "because"));
        }

        List<string> swallowed = Verdict.Objects(failed, "swallowed");
        Check(ref failures, "1 avalée", 1, swallowed.Count);
        if (swallowed.Count == 1)
            Check(ref failures, "source", "LabApi", Verdict.Field(swallowed[0], "source"));

        // Un accolade à l'intérieur d'une chaîne ne doit pas casser le découpage du tableau.
        const string tricky =
            """
            {"id":"A.d","outcome":"failed","durationTicks":1,"failures":[{"assert":"X","expected":"{ pas un objet }","observed":"]","afterTicks":0}],"swallowed":[]}
            """;

        List<string> trickyFails = Verdict.Objects(tricky, "failures");
        Check(ref failures, "accolade dans une chaîne", 1, trickyFails.Count);
        if (trickyFails.Count == 1)
            Check(ref failures, "expected littéral", "{ pas un objet }", Verdict.Field(trickyFails[0], "expected"));

        // La clé ne doit pas être confondue avec un texte qui la contient.
        const string decoy =
            """
            {"id":"\"outcome\":\"passed\"","outcome":"failed","durationTicks":0,"failures":[],"swallowed":[]}
            """;

        Check(ref failures, "clé leurre", "failed", Verdict.Field(decoy, "outcome"));

        // Dérivation du verdict.
        Verdict all = Verdict.Parse(new[]
        {
            """{"kind":"plan","list":false,"tests":[{"id":"A.b","collection":"C"},{"id":"A.c","collection":"C"}]}""",
            passed,
            failed,
            """{"kind":"summary","passed":1,"failed":1,"skipped":0,"errors":0}"""
        });

        Check(ref failures, "résumé présent", true, all.HasSummary);
        Check(ref failures, "passés", 1, all.Passed);
        Check(ref failures, "échoués", 1, all.Failed);
        Check(ref failures, "lignes rendues", 2, all.Lines.Count);
        Check(ref failures, "aucune erreur de protocole", 0, all.Problems.Count);
        Check(ref failures, "code échec assertion", 1, all.ExitCode(false));

        const string plan = """{"kind":"plan","list":false,"tests":[{"id":"A.b","collection":"C"}]}""";
        const string summary = """{"kind":"summary","passed":1,"failed":0,"skipped":0,"errors":0}""";
        var valid = Verdict.Parse(new[] { plan, passed, summary });
        Check(ref failures, "suite valide", 0, valid.ExitCode(false));
        foreach (var corrupt in new[]
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
            Check(ref failures, "verdict corrompu rejeté", 2, Verdict.Parse(corrupt).ExitCode(false));
        var skip = Verdict.Parse(new[] { plan,
            """{"id":"A.b","collection":"C","outcome":"skipped","skip":"raison"}""",
            """{"kind":"summary","passed":0,"failed":0,"skipped":1,"errors":0}""" });
        Check(ref failures, "skip accepté", 0, skip.ExitCode(false));
        Check(ref failures, "skip refusé", 1, skip.ExitCode(true));
        var list = Verdict.Parse(new[] { plan.Replace("false", "true"),
            """{"kind":"summary","passed":0,"failed":0,"skipped":0,"errors":0}""" }, true);
        Check(ref failures, "list", 0, list.ExitCode(false));
        foreach (var path in new[] { "../escape", "a/../../escape", "/absolute", "C:\\escape", "a\\..\\escape" })
        {
            try { Deployment.Under(Path.GetTempPath(), path); Check(ref failures, "traversée refusée", true, false); }
            catch (ArgumentException) { }
        }
        string reports = Path.Combine(Path.GetTempPath(), "labtest-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reports);
        try
        {
            Verdict.Parse(new[] { plan }).WriteReports(reports);
            var xml = System.Xml.Linq.XDocument.Load(Path.Combine(reports, "junit.xml"));
            Check(ref failures, "rapport partiel en erreur", true, xml.Descendants("error").Any());
        }
        finally { Deployment.DeleteOwned(reports); }
        CheckProcessTree(ref failures);

        Verdict truncated = Verdict.Parse(new[] { passed });
        Check(ref failures, "sans résumé", false, truncated.HasSummary);

        Console.WriteLine(failures == 0
            ? "[labtest] selftest : tout passe."
            : "[labtest] selftest : " + failures + " échec(s).");

        return failures == 0 ? 0 : 1;
    }

    private static void Check<T>(ref int failures, string what, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
            return;

        failures++;
        Console.Error.WriteLine("  ÉCHEC " + what + " : attendu <" + expected + ">, obtenu <" + actual + ">");
    }

    internal static System.Diagnostics.ProcessStartInfo SelfStart(string argument, bool session = false)
    {
        string executable = Environment.ProcessPath!;
        var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
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

    private static void CheckProcessTree(ref int failures)
    {
        var start = SelfStart("--selftest-parent", session: true);
        start.RedirectStandardOutput = true;
        using var parent = System.Diagnostics.Process.Start(start)!;
        using var containment = new ProcessContainment();
        System.Diagnostics.Process? child = null;
        try
        {
            containment.Attach(parent);
            int pid = int.Parse(parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()!);
            child = System.Diagnostics.Process.GetProcessById(pid);
            containment.Dispose();
            Check(ref failures, "processus parent arrêté", true, parent.WaitForExit(5000));
            // A dead orphan can remain a zombie briefly under WSL's init.
            bool zombie = OperatingSystem.IsLinux() && File.Exists("/proc/" + pid + "/status") &&
                File.ReadLines("/proc/" + pid + "/status").Any(x => x.StartsWith("State:") && x.Contains('Z'));
            Check(ref failures, "processus enfant arrêté", true, zombie || child.WaitForExit(5000));
        }
        catch (Exception e)
        {
            failures++;
            Console.Error.WriteLine("ÉCHEC arrêt de l'arbre: " + e.Message);
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
