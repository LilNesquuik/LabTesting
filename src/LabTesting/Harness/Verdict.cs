using System.Globalization;
using System.Text;

namespace LabTesting;

/// <summary>
/// How a test ended.
/// </summary>
public enum Outcome
{
    /// <summary>The test ran and recorded no failure.</summary>
    Passed,

    /// <summary>The test ran and recorded at least one failure.</summary>
    Failed,

    /// <summary>The test was not run, either by request or because its signature is unusable.</summary>
    Skipped,

    /// <summary>The harness could not run the test meaningfully. Maps to exit code 2.</summary>
    Error
}

/// <summary>
/// Writes the machine-readable result file: one JSON object per line, flushed as each test ends,
/// followed by a summary line.
/// </summary>
/// <remarks>
/// A file is used rather than the TCP console channel because the console reader waits for bytes
/// in an unbounded loop (TcpConsole.cs:123-126): a corrupted header or a peer that closes freezes
/// it. Basing a build result on that would mean basing it on a potential deadlock.
/// <para>
/// Exit codes are derived from this file by the out-of-process runner: 0 when everything passes,
/// 1 on test failures, 2 on a harness error, and 3 when the summary line is missing, which means
/// the server died before finishing.
/// </para>
/// </remarks>
public sealed class Verdict : IDisposable
{
    private readonly StreamWriter _writer;

    /// <summary>
    /// Creates the result file, replacing any previous one, and creates its directory if needed.
    /// </summary>
    /// <param name="path">Full path of the result file.</param>
    public Verdict(string path)
    {
        Path = path;
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = false
        };
    }

    /// <summary>
    /// Gets the full path of the result file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the number of tests that passed so far.
    /// </summary>
    public int Passed { get; private set; }

    /// <summary>
    /// Gets the number of tests that failed so far.
    /// </summary>
    public int Failed { get; private set; }

    /// <summary>
    /// Gets the number of tests skipped so far.
    /// </summary>
    public int Skipped { get; private set; }

    /// <summary>
    /// Gets the number of tests that ended in a harness error so far.
    /// </summary>
    public int Errors { get; private set; }

    /// <summary>Writes the complete expected plan before any test runs.</summary>
    public void WritePlan(IReadOnlyList<TestCase> plan, bool listOnly)
    {
        var sb = new StringBuilder("{");
        Field(sb, "kind", "plan").Append(',');
        Bool(sb, "list", listOnly).Append(',');
        Field(sb, "frameworkVersion", typeof(Verdict).Assembly.FullName).Append(',');
        Field(sb, "serverVersion", GameCore.Version.VersionString).Append(',');
        Field(sb, "labapiVersion", typeof(LabApi.Loader.PluginLoader).Assembly.FullName).Append(',');
        Field(sb, "harmonyVersion", typeof(HarmonyLib.Harmony).Assembly.FullName).Append(',');
        sb.Append("\"tests\":[");
        for (int i = 0; i < plan.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var t = plan[i];
            sb.Append('{');
            Field(sb, "id", t.Id).Append(',');
            Field(sb, "collection", t.Collection).Append(',');
            Field(sb, "assembly", t.Fixture.Assembly.FullName);
            sb.Append('}');
        }
        sb.Append("]}");
        WriteLine(sb.ToString());
    }

    /// <summary>
    /// Appends one line describing a finished test, and flushes it immediately so that a server
    /// crash cannot lose the results already obtained.
    /// </summary>
    /// <param name="test">The test that ran.</param>
    /// <param name="context">Its context, holding the failures and swallowed exceptions.</param>
    /// <param name="outcome">How it ended.</param>
    /// <param name="durationTicks">How many ticks it took.</param>
    /// <param name="appliedIsolation">The isolation level actually used.</param>
    /// <param name="escalated">Whether that level was raised because a previous test drifted.</param>
    /// <param name="durationMilliseconds">Wall-clock duration, zero when it was not measured.</param>
    public void WriteTest(
        TestCase test,
        TestContext context,
        Outcome outcome,
        int durationTicks,
        Dirty appliedIsolation,
        bool escalated,
        int durationMilliseconds = 0)
    {
        switch (outcome)
        {
            case Outcome.Passed: Passed++; break;
            case Outcome.Failed: Failed++; break;
            case Outcome.Skipped: Skipped++; break;
            default: Errors++; break;
        }

        var sb = new StringBuilder(256);
        sb.Append('{');
        Field(sb, "id", test.Id).Append(',');
        Field(sb, "collection", test.Collection).Append(',');
        Field(sb, "outcome", outcome.ToString().ToLowerInvariant()).Append(',');
        Number(sb, "durationTicks", durationTicks).Append(',');
        Number(sb, "durationMilliseconds", durationMilliseconds).Append(',');
        Field(sb, "isolation", appliedIsolation.ToString()).Append(',');
        Bool(sb, "escalated", escalated).Append(',');
        Number(sb, "uptimeRound", World.UptimeRounds).Append(',');

        if (test.Skip != null)
            Field(sb, "skip", test.Skip).Append(',');
        if (context.HarnessError != null)
            Field(sb, "harnessError", context.HarnessError).Append(',');

        sb.Append("\"failures\":[");
        for (int i = 0; i < context.Failures.Count; i++)
        {
            Failure f = context.Failures[i];
            if (i > 0)
                sb.Append(',');
            sb.Append('{');
            Field(sb, "assert", f.Assert).Append(',');
            Field(sb, "expected", f.Expected).Append(',');
            Field(sb, "observed", f.Observed).Append(',');
            Number(sb, "afterTicks", f.AfterTicks);
            if (f.Because != null)
                Field(sb.Append(','), "because", f.Because);
            sb.Append('}');
        }

        sb.Append("],\"swallowed\":[");
        for (int i = 0; i < context.Swallowed.Count; i++)
        {
            SwallowedError s = context.Swallowed[i];
            if (i > 0)
                sb.Append(',');
            sb.Append('{');
            Field(sb, "source", s.Source).Append(',');
            Field(sb, "message", s.Message);
            sb.Append('}');
        }

        sb.Append("]}");
        WriteLine(sb.ToString());
    }

    /// <summary>
    /// Appends the final summary line.
    /// </summary>
    /// <param name="harnessError">A harness-level error that aborted the run, or null.</param>
    /// <remarks>
    /// The presence of this line is what distinguishes a finished suite from a server that died
    /// halfway through, so it is written even when the run failed.
    /// </remarks>
    public void WriteSummary(string? harnessError = null)
    {
        var sb = new StringBuilder(160);
        sb.Append('{');
        Field(sb, "kind", "summary").Append(',');
        Number(sb, "passed", Passed).Append(',');
        Number(sb, "failed", Failed).Append(',');
        Number(sb, "skipped", Skipped).Append(',');
        Number(sb, "errors", Errors);
        if (harnessError != null)
            Field(sb.Append(','), "harnessError", harnessError);
        sb.Append('}');
        WriteLine(sb.ToString());
    }

    private void WriteLine(string line)
    {
        _writer.WriteLine(line);
        _writer.Flush();
    }

    /// <summary>
    /// Closes the result file.
    /// </summary>
    public void Dispose() => _writer.Dispose();

    private static StringBuilder Field(StringBuilder sb, string name, string value) =>
        sb.Append('"').Append(name).Append("\":").Append(Quote(value));

    private static StringBuilder Number(StringBuilder sb, string name, int value) =>
        sb.Append('"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));

    private static StringBuilder Bool(StringBuilder sb, string name, bool value) =>
        sb.Append('"').Append(name).Append("\":").Append(value ? "true" : "false");

    private static string Quote(string? value)
    {
        if (value == null)
            return "null";

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
