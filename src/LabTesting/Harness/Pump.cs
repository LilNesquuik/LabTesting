using UnityEngine;

namespace LabTesting;

/// <summary>
/// The clock of the framework: a permanent MonoBehaviour that counts server ticks, evaluates
/// pending waits and resumes await continuations on the Unity main thread.
/// </summary>
/// <remarks>
/// The pump does three things:
/// <list type="number">
///   <item><description>counts ticks in FixedUpdate and frames in Update;</description></item>
///   <item><description>samples pending waits at the pace of the server rather than in a tight loop;</description></item>
///   <item><description>installs a <see cref="SynchronizationContext"/> so that every continuation
///   resumes on the main thread between two ticks, which means a test body never runs concurrently
///   with game code.</description></item>
/// </list>
/// </remarks>
public sealed class Pump : MonoBehaviour
{
    private static Pump? _instance;
    private static int _mainThread;

    private readonly List<Waiter> _tickWaiters = [];
    private readonly List<Waiter> _frameWaiters = [];
    private readonly List<Waiter> _ready = [];
    private PumpContext _context = null!;
    private SynchronizationContext? _previousContext;

    /// <summary>
    /// Gets the current tick number, incremented once per FixedUpdate. This is the time unit every
    /// asynchronous assertion works in.
    /// </summary>
    public static long Tick { get; private set; }

    /// <summary>
    /// Gets the current frame number, incremented once per Update. Frames matter for dummy input,
    /// because an emulated click only lives for a single frame.
    /// </summary>
    public static long Frame { get; private set; }

    /// <summary>
    /// Gets the running pump.
    /// </summary>
    /// <exception cref="InvalidOperationException">The pump has not been installed.</exception>
    public static Pump Instance =>
        _instance ?? throw new InvalidOperationException("The pump is not installed.");

    /// <summary>
    /// Gets a value indicating whether the pump is installed and ticking.
    /// </summary>
    public static bool Installed => _instance != null;

    /// <summary>
    /// Creates the pump on a permanent GameObject, or returns the existing one. Called once, from
    /// the plugin entry point.
    /// </summary>
    public static Pump Install()
    {
        if (_instance != null)
            return _instance;

        GameObject go = new GameObject("LabTesting.Pump");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<Pump>();
        return _instance;
    }

    private void Awake()
    {
        _mainThread = Thread.CurrentThread.ManagedThreadId;

        _previousContext = SynchronizationContext.Current;
        _context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(_context);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(SynchronizationContext.Current, _context))
            SynchronizationContext.SetSynchronizationContext(_previousContext);

        _instance = null;
    }

    private void FixedUpdate()
    {
        Tick++;
        Poll(_tickWaiters);
        _context.Drain();
    }

    private void Update()
    {
        Frame++;
        Poll(_frameWaiters);
        _context.Drain();
    }

    private void Poll(List<Waiter> waiters)
    {
        if (waiters.Count == 0)
            return;

        _ready.Clear();
        for (int i = waiters.Count - 1; i >= 0; i--)
        {
            Waiter w = waiters[i];
            bool done;
            try
            {
                done = w.IsDone();
            }
            catch (Exception e)
            {
                // A test predicate is allowed to throw: end the wait and let the
                // calling assertion report it.
                w.Error = e;
                done = true;
            }

            if (!done)
                continue;

            waiters.RemoveAt(i);
            _ready.Add(w);
        }

        // Completion happens after removal, so a continuation cannot re-enter the list while it is
        // being iterated.
        for (int i = 0; i < _ready.Count; i++)
            _ready[i].Complete();

        _ready.Clear();
    }

    /// <summary>
    /// Returns a task that completes on the first FixedUpdate where <paramref name="isDone"/>
    /// returns true.
    /// </summary>
    internal static Task WaitTick(Func<bool> isDone) => Enqueue(Instance._tickWaiters, isDone);

    /// <summary>
    /// Returns a task that completes on the first Update where <paramref name="isDone"/> returns
    /// true.
    /// </summary>
    internal static Task WaitFrame(Func<bool> isDone) => Enqueue(Instance._frameWaiters, isDone);

    private static Task Enqueue(List<Waiter> waiters, Func<bool> isDone)
    {
        // Poll reads the waiter lists on the main thread without a lock.
        if (Thread.CurrentThread.ManagedThreadId != _mainThread)
            throw new InvalidOperationException(
                "[LabTesting] a wait was started off the server main thread. Remove ConfigureAwait(false) "
                + "from tests, and do not call Expect or World from inside Task.Run.");

        Waiter waiter = new Waiter(isDone, TestContext.Current);
        waiters.Add(waiter);
        return waiter.Task;
    }

    /// <summary>
    /// Cancels every wait that belongs to a context other than the current one.
    /// </summary>
    /// <remarks>
    /// A test that exceeded its timeout leaves a task running that cannot be aborted. Cancelling
    /// its waits makes it terminate at its next await instead of continuing to write into the next
    /// test's context. Waits owned by the runner itself are left alone.
    /// </remarks>
    internal static void CancelOrphans()
    {
        if (_instance == null)
            return;

        Sweep(_instance._tickWaiters);
        Sweep(_instance._frameWaiters);
    }

    private static void Sweep(List<Waiter> waiters)
    {
        for (int i = waiters.Count - 1; i >= 0; i--)
        {
            Waiter w = waiters[i];

            // A null owner means the runner itself is waiting, which is never orphaned.
            if (w.Owner == null)
                continue;

            if (!w.Owner.Abandoned && ReferenceEquals(w.Owner, TestContext.Current))
                continue;

            waiters.RemoveAt(i);
            w.Cancel();
        }
    }

    /// <summary>
    /// One pending wait: a predicate, the context that created it, and the task handed to the
    /// caller.
    /// </summary>
    private sealed class Waiter
    {
        private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Waiter(Func<bool> isDone, TestContext? owner)
        {
            IsDone = isDone;
            Owner = owner;
        }

        public Func<bool> IsDone { get; }

        public TestContext? Owner { get; }

        public Exception? Error { get; set; }

        public Task Task => _tcs.Task;

        public void Complete()
        {
            if (Error != null)
                _tcs.TrySetException(Error);
            else
                _tcs.TrySetResult(true);
        }

        public void Cancel() => _tcs.TrySetCanceled();
    }

    /// <summary>
    /// Synchronization context of the harness. Continuations are queued and drained by the pump on
    /// the main thread.
    /// </summary>
    /// <remarks>
    /// Combined with RunContinuationsAsynchronously on the waiter side, queueing rules out any
    /// re-entrancy from the polling loop: completing a wait never runs user code inline.
    /// </remarks>
    private sealed class PumpContext : SynchronizationContext
    {
        // Cap per drain so that a runaway test, for instance one looping on await
        // Task.Yield(), cannot freeze the server.
        private const int MaxPerDrain = 512;

        private readonly Queue<KeyValuePair<SendOrPostCallback, object?>> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
                _queue.Enqueue(new KeyValuePair<SendOrPostCallback, object?>(d, state));
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        /// <summary>
        /// Runs queued continuations, up to <see cref="MaxPerDrain"/> per call.
        /// </summary>
        public void Drain()
        {
            for (int i = 0; i < MaxPerDrain; i++)
            {
                KeyValuePair<SendOrPostCallback, object?> item;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                        return;

                    item = _queue.Dequeue();
                }

                try
                {
                    item.Key(item.Value);
                }
                catch (AssertionAbort)
                {
                    // The verdict is already recorded; the throw only ended the step.
                }
                catch (TaskCanceledException)
                {
                    // Orphaned task from a test that exceeded its timeout.
                }
                catch (Exception e)
                {
                    LabApi.Features.Console.Logger.Error("[LabTesting] continuation: " + e);
                }
            }
        }
    }
}
