
namespace LabTesting;

/// <summary>
/// One failed assertion, as it appears in the <c>failures</c> array of the verdict file.
/// </summary>
public sealed class Failure
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Failure"/> class.
    /// </summary>
    /// <param name="assert">Name of the assertion that failed, for example <c>Eventually</c>.</param>
    /// <param name="expected">What the assertion required.</param>
    /// <param name="observed">What it saw instead.</param>
    /// <param name="afterTicks">Ticks elapsed since the start of the test.</param>
    /// <param name="because">Caller-supplied description, if any.</param>
    public Failure(string assert, string expected, string observed, int afterTicks, string? because)
    {
        Assert = assert;
        Expected = expected;
        Observed = observed;
        AfterTicks = afterTicks;
        Because = because;
    }

    /// <summary>
    /// Gets the name of the assertion that failed.
    /// </summary>
    public string Assert { get; }

    /// <summary>
    /// Gets what the assertion required.
    /// </summary>
    public string Expected { get; }

    /// <summary>
    /// Gets what the assertion saw instead.
    /// </summary>
    public string Observed { get; }

    /// <summary>
    /// Gets the number of ticks elapsed between the start of the test and the failure.
    /// </summary>
    public int AfterTicks { get; }

    /// <summary>
    /// Gets the caller-supplied description of the expectation, or null.
    /// </summary>
    public string? Because { get; }
}

/// <summary>
/// An exception that LabAPI caught and turned into a log line instead of propagating.
/// </summary>
public sealed class SwallowedError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SwallowedError"/> class.
    /// </summary>
    /// <param name="source">Assembly name reported by the logger.</param>
    /// <param name="message">Log message, including the stack trace when present.</param>
    public SwallowedError(string source, string message)
    {
        Source = source;
        Message = message;
    }

    /// <summary>
    /// Gets the assembly name reported by the logger. On the swallow path this is always
    /// <c>LabApi</c>; the faulty plugin is named inside <see cref="Message"/>.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the log message, including the stack trace when the logger produced one.
    /// </summary>
    public string Message { get; }
}

/// <summary>
/// Thrown by a failed assertion for the sole purpose of ending the current step.
/// </summary>
/// <remarks>
/// The verdict never travels in an exception. It is written to <see cref="TestContext"/> first,
/// so a swallowed throw still leaves a recorded failure.
/// </remarks>
public sealed class AssertionAbort : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssertionAbort"/> class.
    /// </summary>
    /// <param name="message">Human-readable description of the failure.</param>
    public AssertionAbort(string message) : base(message)
    {
    }
}

/// <summary>
/// Everything the runner knows about the test currently executing: its identity, its declared
/// isolation, and what it has recorded so far.
/// </summary>
public sealed class TestContext
{
    private readonly List<Failure> _failures = [];
    private readonly List<SwallowedError> _swallowed = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TestContext"/> class.
    /// </summary>
    /// <param name="id">Stable identifier of the test, as reported in the verdict.</param>
    /// <param name="collection">Collection the test belongs to.</param>
    /// <param name="isolation">Isolation level actually applied, which may be higher than declared.</param>
    /// <param name="perf">Whether the test runs in performance mode.</param>
    public TestContext(string id, string collection, Dirty isolation, bool perf)
    {
        Id = id;
        Collection = collection;
        Isolation = isolation;
        Perf = perf;
    }

    /// <summary>
    /// Gets the context of the test currently running, or null between two tests.
    /// </summary>
    public static TestContext? Current { get; internal set; }

    /// <summary>
    /// Gets the stable identifier of the test.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the collection the test belongs to.
    /// </summary>
    public string Collection { get; }

    /// <summary>
    /// Gets the isolation level actually applied, which may be higher than the declared one when
    /// the previous test drifted.
    /// </summary>
    public Dirty Isolation { get; }

    /// <summary>
    /// Gets a value indicating whether the test runs in performance mode, in which event
    /// assertions are rejected.
    /// </summary>
    public bool Perf { get; }

    /// <summary>
    /// Gets a value indicating whether the test exceeded its timeout.
    /// </summary>
    /// <remarks>
    /// A .NET task cannot be aborted. When a test times out the runner marks its context
    /// abandoned, so the orphaned task stops recording into a context that no longer owns the
    /// verdict, and cancels its pending waits so it terminates at its next await.
    /// </remarks>
    public bool Abandoned { get; internal set; }

    /// <summary>
    /// Gets the failures recorded so far.
    /// </summary>
    public IReadOnlyList<Failure> Failures => _failures;

    /// <summary>
    /// Gets the swallowed exceptions captured during this test.
    /// </summary>
    public IReadOnlyList<SwallowedError> Swallowed => _swallowed;

    /// <summary>
    /// Gets the harness error that prevented the test from producing a meaningful result, or null.
    /// A test reporting one is an error rather than a failure, and maps to exit code 2.
    /// </summary>
    public string? HarnessError { get; internal set; }

    /// <summary>
    /// Gets the tick at which the test started, used to date failures.
    /// </summary>
    public long StartTick { get; internal set; }

    internal void AddFailure(Failure failure)
    {
        if (!Abandoned)
            _failures.Add(failure);
    }

    internal void AddSwallowed(SwallowedError error)
    {
        if (!Abandoned)
            _swallowed.Add(error);
    }

    /// <summary>
    /// Records a failure in the ambient context, then throws to end the current step.
    /// </summary>
    /// <exception cref="AssertionAbort">Always thrown, after the failure has been recorded.</exception>
    internal static void Fail(string assert, string expected, string observed, string? because)
    {
        TestContext? ctx = Current;
        int afterTicks = ctx == null ? 0 : (int)(Pump.Tick - ctx.StartTick);
        ctx?.AddFailure(new Failure(assert, expected, observed, afterTicks, because));

        string detail = assert + ": expected " + expected + ", actual " + observed;
        if (!string.IsNullOrEmpty(because))
            detail += " — " + because;

        throw new AssertionAbort(detail);
    }
}

/// <summary>
/// Access to the exceptions LabAPI swallowed while the current test was running.
/// </summary>
public static class Swallowed
{
    /// <summary>
    /// Gets the exceptions captured since the current test started. Empty between tests.
    /// </summary>
    public static IReadOnlyList<SwallowedError> DuringCurrentTest =>
        TestContext.Current?.Swallowed ?? [];

    /// <summary>
    /// Fails the test if LabAPI swallowed any exception while it was running.
    /// </summary>
    /// <param name="settle">
    /// How long to wait before reading. Defaults to one tick.
    /// </param>
    /// <remarks>
    /// Log delivery is delayed by at least one frame, so this method yields before reading.
    /// </remarks>
    public static async Task AssertNone(Ticks settle = default)
    {
        await Expect.Tick(settle.Count > 0 ? settle.Count : 1);

        IReadOnlyList<SwallowedError> errors = DuringCurrentTest;
        if (errors.Count == 0)
            return;

        TestContext.Fail(
            "Swallowed.AssertNone",
            "no swallowed exception",
            errors.Count + ": " + errors[0].Message,
            null);
    }
}

/// <summary>
/// Console sink that captures error lines and attaches them to the running test.
/// </summary>
/// <remarks>
/// This type must never throw. An IOutput that throws, or whose Available() returns false, is
/// removed from ServerConsole.ConsoleOutputs without any warning, which would silently stop the
/// capture for the rest of the run.
/// </remarks>
internal sealed class SwallowedSink : IOutput
{
    private const string ErrorPrefix = "[ERROR] ";

    private static readonly string HarnessAssembly =
        typeof(SwallowedSink).Assembly.GetName().Name;

    /// <inheritdoc/>
    public string OutputId => "LabTesting.SwallowedSink";

    /// <inheritdoc/>
    public void Print(string text)
    {
        try
        {
            // The game calls this logging hook and is free to hand us null.
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (text == null || !text.StartsWith(ErrorPrefix, StringComparison.Ordinal))
                return;

            TestContext? ctx = TestContext.Current;
            if (ctx == null)
                return;

            // Lines are formatted as "[ERROR] [assembly] message".
            string rest = text.Substring(ErrorPrefix.Length);
            string source = "?";
            if (rest.StartsWith("[", StringComparison.Ordinal))
            {
                int close = rest.IndexOf(']');
                if (close > 0)
                {
                    source = rest.Substring(1, close - 1);
                    rest = rest.Substring(close + 1).TrimStart();
                }
            }

            // Delivery is delayed by a frame, so an error the harness logs during
            // teardown arrives after Current moved to the next test. Lines coming
            // from the harness assembly are dropped.
            if (string.Equals(source, HarnessAssembly, StringComparison.Ordinal))
                return;

            ctx.AddSwallowed(new SwallowedError(source, rest));
        }
        catch
        {
            // Never throw here: the game would unregister the sink without any
            // warning.
        }
    }

    /// <inheritdoc/>
    public void Print(string text, ConsoleColor c) => Print(text);

    /// <inheritdoc/>
    public void Print(string text, ConsoleColor c, UnityEngine.Color rgbColor) => Print(text);

    /// <inheritdoc/>
    public bool Available() => true;

    /// <summary>
    /// Registers the sink with the server console.
    /// </summary>
    public void Install() => ServerConsole.ConsoleOutputs.TryAdd(OutputId, this);

    /// <summary>
    /// Unregisters the sink, stopping capture until <see cref="Install"/> is called again.
    /// </summary>
    public void Uninstall() => ServerConsole.ConsoleOutputs.TryRemove(OutputId, out _);
}
