using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace LabTesting;

/// <summary>
/// Asynchronous assertions: the core of the framework.
/// </summary>
/// <remarks>
/// Every predicate is sampled in FixedUpdate, so the harness observes at the pace of the server
/// rather than its own, and never spins. Durations are expressed in ticks rather than
/// milliseconds, which keeps a test independent of how busy the machine is.
/// </remarks>
public static class Expect
{
    /// <summary>
    /// Yields for a number of server ticks.
    /// </summary>
    /// <param name="count">Number of ticks to yield. Values below one are treated as one.</param>
    public static Task Tick(int count = 1)
    {
        long target = Pump.Tick + Math.Max(1, count);
        return Pump.WaitTick(() => Pump.Tick >= target);
    }

    /// <summary>
    /// Yields for a number of frames.
    /// </summary>
    /// <param name="count">Number of frames to yield. Values below one are treated as one.</param>
    /// <remarks>
    /// Frames matter for dummy input: an emulated click only lives for one frame
    /// (DummyKeyEmulator.cs:75-93), so the test has to give the server a frame to observe it.
    /// </remarks>
    public static Task Frame(int count = 1)
    {
        long target = Pump.Frame + Math.Max(1, count);
        return Pump.WaitFrame(() => Pump.Frame >= target);
    }

    /// <summary>
    /// Waits until the predicate becomes true, and fails the test if it has not within the budget.
    /// </summary>
    /// <param name="predicate">Condition sampled once per tick. It may throw; that counts as false.</param>
    /// <param name="within">How many ticks to wait.</param>
    /// <param name="because">What the condition means, shown in the verdict on failure.</param>
    public static async Task Eventually(Func<bool> predicate, Ticks within, string? because = null)
    {
        long deadline = Pump.Tick + within.Count;
        bool satisfied = false;
        Exception? last = null;

        await Pump.WaitTick(() =>
        {
            satisfied = Evaluate(predicate, ref last);
            return satisfied || Pump.Tick >= deadline;
        });

        if (!satisfied)
            TestContext.Fail("Eventually", Describe(predicate, because), Observed(last), because);
    }

    /// <summary>
    /// Checks that the predicate holds on every tick for the whole duration. Fails on the first
    /// tick where it does not.
    /// </summary>
    /// <param name="predicate">Invariant sampled once per tick.</param>
    /// <param name="during">How many ticks the invariant must hold.</param>
    /// <param name="because">What the invariant means, shown in the verdict on failure.</param>
    public static async Task Always(Func<bool> predicate, Ticks during, string? because = null)
    {
        long deadline = Pump.Tick + during.Count;
        bool broken = false;
        Exception? last = null;

        await Pump.WaitTick(() =>
        {
            broken = !Evaluate(predicate, ref last);
            return broken || Pump.Tick >= deadline;
        });

        if (broken)
            TestContext.Fail("Always", Describe(predicate, because), Observed(last), because);
    }

    /// <summary>
    /// Checks that the predicate never becomes true for the whole duration. Fails on the first
    /// tick where it does.
    /// </summary>
    /// <param name="predicate">Condition sampled once per tick.</param>
    /// <param name="during">How many ticks to watch.</param>
    /// <param name="because">What the condition means, shown in the verdict on failure.</param>
    public static async Task Never(Func<bool> predicate, Ticks during, string? because = null)
    {
        long deadline = Pump.Tick + during.Count;
        bool happened = false;
        Exception? last = null;

        await Pump.WaitTick(() =>
        {
            happened = Evaluate(predicate, ref last);
            return happened || Pump.Tick >= deadline;
        });

        if (happened)
            TestContext.Fail("Never", "never " + Describe(predicate, because), "happened", because);
    }

    /// <summary>
    /// Waits for a LabAPI event to be raised and returns its arguments.
    /// </summary>
    /// <typeparam name="T">Event arguments type, which identifies the event.</typeparam>
    /// <param name="within">How many ticks to wait.</param>
    /// <param name="where">Optional filter; the first matching raise is captured.</param>
    /// <returns>The captured event arguments.</returns>
    /// <remarks>
    /// The type alone is enough to identify the event: LabAPI declares exactly one event per
    /// arguments type, with no duplicate and no orphan on either side. The subscription is set up
    /// and torn down around the wait, so nothing stays attached after the assertion.
    /// </remarks>
    public static async Task<T> Event<T>(Ticks within, Func<T, bool>? where = null) where T : EventArgs
    {
        RefusePerf("Expect.Event<" + typeof(T).Name + ">");

        T? captured = null;
        using (EventCatalog.Hook<T>(ev =>
               {
                   if (captured == null && (where == null || where(ev)))
                       captured = ev;
               }))
        {
            long deadline = Pump.Tick + within.Count;
            await Pump.WaitTick(() => captured != null || Pump.Tick >= deadline);
        }

        if (captured == null)
            TestContext.Fail("Event<" + typeof(T).Name + ">", "raised within " + within, "not raised", null);

        return captured!;
    }

    /// <summary>
    /// Starts recording two event types until the returned recorder is disposed.
    /// </summary>
    public static IEventRecorder Events<T1, T2>()
        where T1 : EventArgs
        where T2 : EventArgs
    {
        RefusePerf("Expect.Events");
        var recorder = new EventRecorder();
        recorder.Watch<T1>();
        recorder.Watch<T2>();
        return recorder;
    }

    /// <summary>
    /// Starts recording three event types until the returned recorder is disposed.
    /// </summary>
    public static IEventRecorder Events<T1, T2, T3>()
        where T1 : EventArgs
        where T2 : EventArgs
        where T3 : EventArgs
    {
        RefusePerf("Expect.Events");
        var recorder = new EventRecorder();
        recorder.Watch<T1>();
        recorder.Watch<T2>();
        recorder.Watch<T3>();
        return recorder;
    }

    /// <summary>
    /// Starts recording four event types until the returned recorder is disposed.
    /// </summary>
    public static IEventRecorder Events<T1, T2, T3, T4>()
        where T1 : EventArgs
        where T2 : EventArgs
        where T3 : EventArgs
        where T4 : EventArgs
    {
        RefusePerf("Expect.Events");
        var recorder = new EventRecorder();
        recorder.Watch<T1>();
        recorder.Watch<T2>();
        recorder.Watch<T3>();
        recorder.Watch<T4>();
        return recorder;
    }

    private static bool Evaluate(Func<bool> predicate, ref Exception? last)
    {
        try
        {
            return predicate();
        }
        catch (Exception e)
        {
            last = e;
            return false;
        }
    }

    private static string Describe(Func<bool> predicate, string? because) =>
        because ?? predicate.Method.Name;

    private static string Observed(Exception? last) =>
        last == null ? "false on timeout" : "predicate threw: " + last.GetType().Name;

    /// <summary>
    /// Rejects event assertions inside a performance test.
    /// </summary>
    /// <remarks>
    /// LabAPI returns from a dispatch before allocating anything when an event has no subscriber
    /// (EventManager.cs:36-39), whereas a single subscriber costs one GetInvocationList() array
    /// per dispatch (EventManager.cs:40). Subscribing during a measurement would change exactly
    /// what is being measured.
    /// <para>
    /// The rejection happens on the first call rather than when the plan is built, because
    /// detecting it statically would mean decompiling the async state machine the compiler
    /// generates for the test body. It is reported as a harness error, never silently ignored.
    /// </para>
    /// </remarks>
    private static void RefusePerf(string what)
    {
        TestContext? ctx = TestContext.Current;
        if (ctx == null || !ctx.Perf)
            return;

        ctx.HarnessError = what + " is not allowed inside a [Perf] test (D11).";
        throw new AssertionAbort(ctx.HarnessError);
    }
}

/// <summary>
/// Records LabAPI events as they are raised, so that a test can assert on how many arrived and in
/// what order. Dispose to unsubscribe.
/// </summary>
public interface IEventRecorder : IDisposable
{
    /// <summary>
    /// Returns how many times the given event was raised since recording started.
    /// </summary>
    int CountOf<T>() where T : EventArgs;

    /// <summary>
    /// Fails the test unless every watched type was raised at least once, in the order they were
    /// declared.
    /// </summary>
    void AssertOrdered();

    /// <summary>
    /// Fails the test if the given event was raised at all.
    /// </summary>
    void AssertNone<T>() where T : EventArgs;
}

internal sealed class EventRecorder : IEventRecorder
{
    private readonly List<IDisposable> _hooks = new List<IDisposable>();
    private readonly List<Type> _watched = new List<Type>();
    private readonly List<Type> _seen = new List<Type>();

    public void Watch<T>() where T : EventArgs
    {
        _watched.Add(typeof(T));
        _hooks.Add(EventCatalog.Hook<T>(_ => _seen.Add(typeof(T))));
    }

    /// <inheritdoc/>
    public int CountOf<T>() where T : EventArgs
    {
        int n = 0;
        for (int i = 0; i < _seen.Count; i++)
        {
            if (_seen[i] == typeof(T))
                n++;
        }

        return n;
    }

    /// <inheritdoc/>
    public void AssertOrdered()
    {
        int cursor = 0;
        for (int i = 0; i < _watched.Count; i++)
        {
            int at = _seen.IndexOf(_watched[i], cursor);
            if (at < 0)
            {
                TestContext.Fail(
                    "AssertOrdered",
                    string.Join(" → ", Names(_watched)),
                    string.Join(" → ", Names(_seen)),
                    _watched[i].Name + " missing or out of sequence");
                return;
            }

            cursor = at + 1;
        }
    }

    /// <inheritdoc/>
    public void AssertNone<T>() where T : EventArgs
    {
        int n = CountOf<T>();
        if (n > 0)
            TestContext.Fail("AssertNone<" + typeof(T).Name + ">", "0", n.ToString(), null);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        for (int i = 0; i < _hooks.Count; i++)
            _hooks[i].Dispose();
        _hooks.Clear();
    }

    private static string[] Names(List<Type> types)
    {
        var names = new string[types.Count];
        for (int i = 0; i < types.Count; i++)
            names[i] = types[i].Name;
        return names;
    }
}

/// <summary>
/// Maps a LabAPI event arguments type to the event that carries it, and subscribes to it by
/// reflection.
/// </summary>
/// <remarks>
/// The table is built once by reflecting over the handler classes in LabApi.Events.Handlers and
/// reading the generic argument of each event's LabEventHandler type. Subscription uses the same
/// recipe LabAPI itself uses for its class-based handlers: GetEvent, CreateDelegate, then
/// AddEventHandler with a null target, since the events are public and static.
/// </remarks>
public static class EventCatalog
{
    private static Dictionary<Type, EventInfo>? _byArgs;

    /// <summary>
    /// Event arguments types declared by LabAPI that the game never raises.
    /// </summary>
    /// <remarks>
    /// Measured by scanning the decompiled game assembly for call sites. Waiting on one of these
    /// would always time out, so the runner treats them as untestable until their raise point is
    /// established. PluginsEnabled is excluded from the list: LabAPI raises it itself, during
    /// plugin loading, which is too early for a test to observe.
    /// </remarks>
    public static readonly string[] NeverRaisedByGame =
    {
        "PlayerSearchedAmmoEventArgs",
        "PlayerSearchedArmorEventArgs",
        "PickupCreatedEventArgs",
        "PickupDestroyedEventArgs",
        "CassieAnnouncingEventArgs",
        "CassieAnnouncedEventArgs"
    };

    private static Dictionary<Type, EventInfo> Table
    {
        get
        {
            if (_byArgs != null)
                return _byArgs;

            var table = new Dictionary<Type, EventInfo>();
            Assembly labApi = typeof(LabApi.Events.Handlers.ServerEvents).Assembly;

            foreach (Type type in labApi.GetTypes())
            {
                if (type.Namespace != "LabApi.Events.Handlers")
                    continue;

                foreach (EventInfo ev in type.GetEvents(BindingFlags.Public | BindingFlags.Static))
                {
                    Type handler = ev.EventHandlerType;
                    if (!handler.IsGenericType)
                        continue;
                    if (handler.GetGenericTypeDefinition() != typeof(LabApi.Events.LabEventHandler<>))
                        continue;

                    table[handler.GetGenericArguments()[0]] = ev;
                }
            }

            _byArgs = table;
            return table;
        }
    }

    /// <summary>
    /// Returns whether a LabAPI event carries the given arguments type.
    /// </summary>
    public static bool IsKnown(Type eventArgs) => Table.ContainsKey(eventArgs);

    /// <summary>
    /// Returns whether the event is declared by LabAPI but never raised by the game.
    /// </summary>
    public static bool IsNeverRaisedByGame(Type eventArgs) =>
        Array.IndexOf(NeverRaisedByGame, eventArgs.Name) >= 0;

    /// <summary>
    /// Gets the number of catalogued events. Useful as a sanity check that the LabAPI version in
    /// use matches expectations.
    /// </summary>
    public static int Count => Table.Count;

    /// <summary>
    /// Subscribes a callback to the LabAPI event identified by <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Event arguments type.</typeparam>
    /// <param name="callback">Called on every raise until the subscription is disposed.</param>
    /// <returns>A handle that unsubscribes when disposed.</returns>
    /// <exception cref="InvalidOperationException">No LabAPI event carries that type.</exception>
    public static IDisposable Hook<T>(Action<T> callback) where T : EventArgs
    {
        if (!Table.TryGetValue(typeof(T), out EventInfo ev))
            throw new InvalidOperationException("No LabAPI event is named " + typeof(T).FullName + ".");

        return new Subscription<T>(ev, callback);
    }

    private sealed class Subscription<T> : IDisposable where T : EventArgs
    {
        private readonly EventInfo _event;
        private readonly Delegate _delegate;
        private readonly Action<T> _callback;
        private bool _disposed;

        public Subscription(EventInfo ev, Action<T> callback)
        {
            _event = ev;
            _callback = callback;
            _delegate = Delegate.CreateDelegate(ev.EventHandlerType, this, nameof(Handle));
            _event.AddEventHandler(null, _delegate);
        }

        private void Handle(T ev)
        {
            try
            {
                _callback(ev);
            }
            catch (Exception e)
            {
                // This handler runs inside LabAPI's per-subscriber try/catch, so throwing here
                // would only produce a lost log line. Record the failure instead.
                TestContext.Current?.AddFailure(
                    new Failure("EventCatalog.Hook", "handler without error", e.GetType().Name, 0, e.Message));
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _event.RemoveEventHandler(null, _delegate);
        }
    }
}
