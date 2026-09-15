
namespace LabTesting;

// Declarative surface of the framework. The names mirror xUnit so that tests read the same way,
// but they live in their own namespace: pure-logic tests can still use the real xUnit package
// without any ambiguity.

/// <summary>
/// Marks a parameterless method as a test case.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the reason the test is skipped. A non-null value keeps the test in the plan
    /// and reports it as skipped instead of running it.
    /// </summary>
    public string? Skip { get; set; }
}

/// <summary>
/// Marks a method as a data-driven test. Each <see cref="InlineDataAttribute"/> applied to the
/// method produces one test case.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TheoryAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the reason the test is skipped. A non-null value keeps the test in the plan
    /// and reports it as skipped instead of running it.
    /// </summary>
    public string? Skip { get; set; }
}

/// <summary>
/// Supplies one set of arguments to a <see cref="TheoryAttribute"/> method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InlineDataAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InlineDataAttribute"/> class.
    /// </summary>
    /// <param name="data">Arguments passed to the test method, in declaration order.</param>
    public InlineDataAttribute(params object[] data) => Data = data;

    /// <summary>
    /// Gets the arguments passed to the test method.
    /// </summary>
    public object[] Data { get; }
}

/// <summary>
/// How much server state a test is allowed to leave behind. The runner sorts the plan by
/// ascending value so that the cheapest cleanups run first and restarts are kept to a minimum.
/// </summary>
public enum Dirty
{
    /// <summary>
    /// The test only creates dummies. Cleanup destroys them, clears the recorded network
    /// segments and resets the item serial generator. This is the default.
    /// </summary>
    Dummies,

    /// <summary>
    /// The test changes round state. Cleanup additionally restarts the round and waits for the
    /// restart barrier.
    /// </summary>
    Round,

    /// <summary>
    /// The test needs a server to itself. Such tests are scheduled last.
    /// </summary>
    /// <remarks>
    /// The in-process harness cannot relaunch itself, so it degrades this level to
    /// <see cref="Round"/>. True quarantine requires the out-of-process runner to start one
    /// server per test.
    /// </remarks>
    Process
}

/// <summary>
/// Declares how much state the test is allowed to leave behind. Without it a test defaults to
/// <see cref="Dirty.Dummies"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IsolationAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IsolationAttribute"/> class.
    /// </summary>
    /// <param name="level">The isolation level required by the test.</param>
    public IsolationAttribute(Dirty level) => Level = level;

    /// <summary>
    /// Gets the isolation level required by the test.
    /// </summary>
    public Dirty Level { get; }
}

/// <summary>
/// Overrides the number of ticks a test may run before the runner gives up on it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TimeoutAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutAttribute"/> class.
    /// </summary>
    /// <param name="ticks">Maximum number of server ticks the test may run for.</param>
    public TimeoutAttribute(int ticks) => Ticks = ticks;

    /// <summary>
    /// Gets the maximum number of server ticks the test may run for.
    /// </summary>
    public int Ticks { get; }
}

/// <summary>
/// Marks a test as a performance measurement. The harness unsubscribes its own listeners for the
/// duration of the test and rejects event assertions.
/// </summary>
/// <remarks>
/// LabAPI returns from a dispatch before allocating anything when an event has no subscriber
/// (EventManager.cs:36-39), while a single subscriber costs one GetInvocationList() array per
/// dispatch (EventManager.cs:40). Observing an event therefore changes the very cost being
/// measured.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PerfAttribute : Attribute
{
}

/// <summary>
/// Groups a test class into a named collection. The collection name appears in the verdict and
/// keeps related tests scheduled together.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CollectionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionAttribute"/> class.
    /// </summary>
    /// <param name="name">Name of the collection.</param>
    public CollectionAttribute(string name) => Name = name;

    /// <summary>
    /// Gets the name of the collection.
    /// </summary>
    public string Name { get; }
}

/// <summary>
/// Runs the test inside a seed scope, so that everything the game derives from
/// <see cref="UnityEngine.Random"/> is reproducible.
/// </summary>
/// <remarks>
/// This only covers what the game actually seeds. Wave player selection, keycard words and role
/// assignment order stay non-deterministic by design; assert on invariants rather than on values.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SeedAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SeedAttribute"/> class.
    /// </summary>
    /// <param name="seed">Seed passed to <see cref="UnityEngine.Random.InitState(int)"/>.</param>
    public SeedAttribute(int seed) => Seed = seed;

    /// <summary>
    /// Gets the seed used for the test.
    /// </summary>
    public int Seed { get; }
}

/// <summary>
/// Implemented by a test class that needs asynchronous setup or teardown. The runner awaits
/// <see cref="InitializeAsync"/> before the test method and <see cref="DisposeAsync"/> after it,
/// even when the test failed.
/// </summary>
public interface IAsyncLifetime
{
    /// <summary>
    /// Runs before the test method.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Runs after the test method, including when it failed.
    /// </summary>
    Task DisposeAsync();
}

/// <summary>
/// A duration expressed in server ticks, the unit every asynchronous assertion works in.
/// </summary>
/// <remarks>
/// A tick is one FixedUpdate. Expressing timeouts in milliseconds would make tests depend on how
/// busy the machine is, because the server only makes progress when it ticks.
/// </remarks>
public readonly struct Ticks
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Ticks"/> structure.
    /// </summary>
    /// <param name="count">Number of ticks. Negative values are clamped to zero.</param>
    public Ticks(int count) => Count = count < 0 ? 0 : count;

    /// <summary>
    /// Gets the number of ticks.
    /// </summary>
    public int Count { get; }

    /// <inheritdoc/>
    public override string ToString() => Count + " ticks";
}

/// <summary>
/// Extension methods that build a <see cref="Ticks"/> value, so that call sites read as
/// <c>30.Ticks()</c> or <c>1.5f.Seconds()</c>.
/// </summary>
public static class TickExtensions
{
    /// <summary>
    /// Returns a duration of <paramref name="n"/> server ticks.
    /// </summary>
    public static Ticks Ticks(this int n) => new(n);

    /// <summary>
    /// Converts seconds to ticks using the current tickrate.
    /// </summary>
    /// <remarks>
    /// The tickrate lives in <c>Application.targetFrameRate</c>, set by
    /// <c>ServerStatic.ServerTickrate</c> (ServerStatic.cs:67) and defaulting to 60
    /// (ServerStatic.cs:95). The result is indicative: prefer ticks when the exact budget matters.
    /// </remarks>
    public static Ticks Seconds(this float s)
    {
        int rate = UnityEngine.Application.targetFrameRate;
        if (rate <= 0)
            rate = 60;
        return new Ticks((int)(s * rate));
    }
}

/// <summary>
/// Synchronous assertions on plain values. The surface is deliberately narrow; anything that has
/// to wait for the server belongs in <see cref="Expect"/>.
/// </summary>
/// <remarks>
/// Every failure is recorded in the ambient <see cref="TestContext"/> before an
/// <see cref="AssertionAbort"/> is thrown. The exception only ends the current step: if it is
/// swallowed, for instance because the assertion ran inside a LabAPI event handler, the verdict
/// has already been written.
/// </remarks>
public static class Assert
{
    /// <summary>
    /// Fails the test unless <paramref name="condition"/> is true.
    /// </summary>
    /// <param name="condition">Condition that must hold.</param>
    /// <param name="because">Short description of what was expected, shown in the verdict.</param>
    public static void True(bool condition, string? because = null)
    {
        if (!condition)
            TestContext.Fail("Assert.True", "true", "false", because);
    }

    /// <summary>
    /// Fails the test unless <paramref name="condition"/> is false.
    /// </summary>
    /// <param name="condition">Condition that must not hold.</param>
    /// <param name="because">Short description of what was expected, shown in the verdict.</param>
    public static void False(bool condition, string? because = null)
    {
        if (condition)
            TestContext.Fail("Assert.False", "false", "true", because);
    }

    /// <summary>
    /// Fails the test unless both values are equal according to the default equality comparer.
    /// </summary>
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            TestContext.Fail("Assert.Equal", Describe(expected), Describe(actual), null);
    }

    /// <summary>
    /// Fails the test if <paramref name="value"/> is null.
    /// </summary>
    /// <param name="value">Value that must not be null.</param>
    /// <param name="because">Short description of what was expected, shown in the verdict.</param>
    public static void NotNull(object? value, string? because = null)
    {
        if (value is null)
            TestContext.Fail("Assert.NotNull", "non-null", "null", because);
    }

    /// <summary>
    /// Fails the test unless <paramref name="action"/> throws <typeparamref name="TEx"/>.
    /// </summary>
    /// <typeparam name="TEx">Expected exception type.</typeparam>
    /// <param name="action">Code expected to throw.</param>
    public static void Throws<TEx>(Action action) where TEx : Exception
    {
        try
        {
            action();
        }
        catch (TEx)
        {
            return;
        }
        catch (Exception e)
        {
            TestContext.Fail("Assert.Throws", typeof(TEx).Name, e.GetType().Name, null);
            return;
        }

        TestContext.Fail("Assert.Throws", typeof(TEx).Name, "no exception", null);
    }

    internal static string Describe(object? value) => value?.ToString() ?? "null";
}
