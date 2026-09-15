using System.Diagnostics;
using System.Reflection;
using LabApi.Features.Console;

namespace LabTesting;

/// <summary>
/// Runs the whole plan, writes the result file, and reports what happened.
/// </summary>
/// <remarks>
/// The loop is itself asynchronous and driven by the pump, so it never blocks the Unity main
/// thread: between two steps the server keeps ticking normally.
/// </remarks>
public static class Runner
{
    /// <summary>
    /// Gets a value indicating whether a suite is currently running.
    /// </summary>
    public static bool Running { get; private set; }

    /// <summary>
    /// Builds the plan, waits for a playable round, runs every test, and writes the summary.
    /// </summary>
    /// <param name="verdictPath">Full path of the result file to write.</param>
    /// <remarks>
    /// Any exception that escapes is recorded as a harness error in the summary rather than being
    /// propagated, so the file always ends with a summary line when the run completed at all.
    /// </remarks>
    public static async Task RunAll(string verdictPath)
    {
        Running = true;
        using (Verdict verdict = new Verdict(verdictPath))
        {
            string? harnessError = null;
            try
            {
                // Yield until the synchronous LabAPI plugin loading cycle has completed.
                await Expect.Frame();
                SuiteRequest request = new SuiteRequest();
                request.CheckPlugins();
                List<TestCase> plan = request.Plan();
                verdict.WritePlan(plan, request.List);
                Logger.Info("[LabTesting] " + plan.Count + " test(s) planned, "
                            + EventCatalog.Count + " LabAPI events catalogued.");

                if (!request.List) await WaitForPlayableRound();
                else
                {
                    // Quitting while FastMenu is still starting can leave native network
                    // threads alive. Wait for the lobby without invoking test fixtures.
                    await Expect.Eventually(() => Mirror.NetworkServer.active && GameCore.RoundStart.singleton != null,
                        3600.Ticks(), "the lobby must be loaded before discovery exits");
                }

                Dirty escalation = Dirty.Dummies;
                foreach (TestCase test in request.List ? [] : plan)
                {
                    escalation = await RunOne(test, verdict, escalation);
                }
            }
            catch (Exception e)
            {
                harnessError = e.ToString();
                Logger.Error("[LabTesting] harness error: " + e);
            }

            verdict.WriteSummary(harnessError);
            Logger.Info("[LabTesting] finished: " + verdict.Passed + " ok, " + verdict.Failed
                        + " failed, " + verdict.Skipped + " skipped, " + verdict.Errors + " errors.");
        }

        Running = false;
    }

    /// <summary>
    /// Waits until the server is up and a round is running, so that tests do not start against a
    /// half-initialised world.
    /// </summary>
    private static async Task WaitForPlayableRound()
    {
        await Expect.Eventually(() => Mirror.NetworkServer.active, 3600.Ticks(),
            "the server must be running");
        await World.ForceRoundStart();
    }

    /// <summary>
    /// Runs one test, writes its line, cleans up, and returns the isolation level the next test
    /// should start from.
    /// </summary>
    private static async Task<Dirty> RunOne(TestCase test, Verdict verdict, Dirty escalation)
    {
        Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        Dirty isolation = test.Isolation;
        bool escalated = false;
        if (escalation > isolation)
        {
            isolation = escalation;
            escalated = true;
        }

        TestContext context = new TestContext(test.Id, test.Collection, isolation, test.Perf)
        {
            StartTick = Pump.Tick
        };

        if (test.Skip != null)
        {
            TestContext.Current = context;
            verdict.WriteTest(test, context, Outcome.Skipped, 0, isolation, escalated);
            TestContext.Current = null;
            return Dirty.Dummies;
        }

        StateFingerprint before = World.Fingerprint();
        TestContext.Current = context;

        Outcome outcome;
        try
        {
            outcome = await Invoke(test, context);
        }
        catch (Exception e)
        {
            context.HarnessError = e.ToString();
            outcome = Outcome.Error;
        }

        context.Abandoned = true;
        Pump.CancelOrphans();
        context.Abandoned = false;

        // Clean up at the applied level, then measure what the test left behind.
        try
        {
            await Isolation.Apply(isolation);
            await Expect.Frame(2); // Flush delayed LabAPI error output before finalizing this test.
        }
        catch (Exception e)
        {
            context.HarnessError = e.ToString();
            Logger.Error("[LabTesting] teardown of " + test.Id + ": " + e);
        }

        if (context.HarnessError != null) outcome = Outcome.Error;
        else if (context.Failures.Count > 0 || context.Swallowed.Count > 0) outcome = Outcome.Failed;
        verdict.WriteTest(test, context, outcome, (int)(Pump.Tick - context.StartTick), isolation, escalated, (int)elapsed.ElapsedMilliseconds);
        TestContext.Current = null;
        context.Abandoned = true;

        StateFingerprint after = World.Fingerprint();
        if (!after.DriftedFrom(before))
            return Dirty.Dummies;

        // The test left more behind than it declared. Escalate for the next one and say so: a
        // test cannot quietly under-declare what it dirties.
        Logger.Warn("[LabTesting] " + test.Id + " DIRTY: " + before + " → " + after
                    + ", escalated to level " + Isolation.Escalate(isolation) + ".");
        return Isolation.Escalate(isolation);
    }

    /// <summary>
    /// Instantiates the fixture, applies the seed and performance scopes, and runs the test body
    /// under its timeout.
    /// </summary>
    private static async Task<Outcome> Invoke(TestCase test, TestContext context)
    {
        object instance = Activator.CreateInstance(test.Fixture)!;
        SeedScope? seed = test.Seed.HasValue ? new SeedScope(test.Seed.Value) : null;
        IDisposable? unsubscribe = test.Perf ? PerfMode.Enter() : null;

        try
        {
            if (instance is IAsyncLifetime lifetime)
                await lifetime.InitializeAsync();

            Task body = RunBody(test, instance);
            Task timeout = Expect.Tick(test.TimeoutTicks);
            Task finished = await Task.WhenAny(body, timeout);

            if (finished != body)
            {
                context.AddFailure(new Failure(
                    "Timeout", "completed within " + test.TimeoutTicks + " ticks", "still running",
                    test.TimeoutTicks, null));
                return Outcome.Failed;
            }

            await body; // rethrows whatever the body threw
        }
        catch (AssertionAbort)
        {
            // The verdict is already recorded; the throw only ended the step.
        }
        catch (TargetInvocationException e) when (e.InnerException is AssertionAbort)
        {
        }
        catch (Exception e)
        {
            Exception real = e is TargetInvocationException tie && tie.InnerException != null
                ? tie.InnerException
                : e;
            context.AddFailure(new Failure(
                "Exception", "no exception", real.GetType().Name, (int)(Pump.Tick - context.StartTick),
                real.ToString()));
            return Outcome.Failed;
        }
        finally
        {
            try
            {
                if (instance is IAsyncLifetime lifetime)
                    await lifetime.DisposeAsync();
            }
            catch (Exception e)
            {
                context.HarnessError = e.ToString();
                Logger.Error("[LabTesting] DisposeAsync of " + test.Id + ": " + e);
            }

            unsubscribe?.Dispose();
            seed?.Dispose();
            (instance as IDisposable)?.Dispose();
        }

        if (context.HarnessError != null)
            return Outcome.Error;

        return context.Failures.Count == 0 && context.Swallowed.Count == 0 ? Outcome.Passed : Outcome.Failed;
    }

    private static Task RunBody(TestCase test, object instance)
    {
        object? returned = test.Method.Invoke(instance, test.Arguments);
        return returned as Task ?? Task.CompletedTask;
    }
}

/// <summary>
/// Detaches the harness for the duration of a performance test.
/// </summary>
/// <remarks>
/// LabAPI returns from a dispatch before allocating anything when an event has no subscriber
/// (EventManager.cs:36-39), whereas a single subscriber costs one GetInvocationList() array per
/// dispatch (:40). Observing during a measurement would change exactly what is being measured.
/// <para>
/// The harness holds a single permanent subscription, the console sink, so that is what gets
/// removed. Event subscriptions are rejected at the call site instead.
/// </para>
/// </remarks>
internal static class PerfMode
{
    /// <summary>
    /// Detaches the harness until the returned scope is disposed.
    /// </summary>
    public static IDisposable Enter() => new Scope();

    private sealed class Scope : IDisposable
    {
        public Scope() => TestPlugin.Sink?.Uninstall();

        public void Dispose() => TestPlugin.Sink?.Install();
    }
}
