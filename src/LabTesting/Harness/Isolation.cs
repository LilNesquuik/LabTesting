using InventorySystem.Items;
using NetworkManagerUtils.Dummies;

namespace LabTesting;

/// <summary>
/// Cleanup performed between tests, one method per isolation level.
/// </summary>
public static class Isolation
{
    /// <summary>
    /// Applies the cleanup for the given level.
    /// </summary>
    /// <param name="level">Isolation level to apply.</param>
    /// <returns>The level actually applied.</returns>
    public static async Task<Dirty> Apply(Dirty level)
    {
        switch (level)
        {
            case Dirty.Dummies:
                CleanDummies();
                await DummiesGone();
                break;

            case Dirty.Round:
                CleanDummies();
                await DummiesGone();
                await World.RestartRound();
                break;

            case Dirty.Process:
                CleanDummies();
                await DummiesGone();
                await World.RestartRound();
                break;
        }

        return level;
    }

    /// <summary>
    /// Cheapest cleanup: destroy the dummies, discard the recorded network segments and reset the
    /// item serial counter so that a rerun produces the same serial numbers.
    /// </summary>
    private static void CleanDummies()
    {
        foreach (TestPlayer player in World.Spawned)
            player.Net.Clear();

        World.Spawned.Clear();

        DummyUtils.DestroyAllDummies();

        ItemSerialGenerator.Reset();
    }

    /// <summary>
    /// Waits, with a bound, until the destroyed dummies have actually left the hub list.
    /// </summary>
    private static Task DummiesGone()
    {
        long deadline = Pump.Tick + 120;
        return Pump.WaitTick(() => !AnyDummy() || Pump.Tick >= deadline);
    }

    private static bool AnyDummy()
    {
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            if (hub != null && hub.IsDummy)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the next isolation level up, used when a test leaves more behind than it declared.
    /// </summary>
    public static Dirty Escalate(Dirty level) =>
        level == Dirty.Dummies ? Dirty.Round : Dirty.Process;
}

/// <summary>
/// Makes the seeded part of the game reproducible for the duration of a test, and restores the
/// previous random state on disposal.
/// </summary>
/// <remarks>
/// This does not make a test deterministic on its own. Wave player selection and keycard words use
/// generators that are never seeded, and role assignment depends on the iteration order of a hash
/// set. Assert on counting invariants rather than on who got what.
/// </remarks>
public sealed class SeedScope : IDisposable
{
    private readonly UnityEngine.Random.State _previous;
    private bool _disposed;

    /// <summary>
    /// Saves the current random state and seeds the generator.
    /// </summary>
    /// <param name="seed">Seed to apply.</param>
    public SeedScope(int seed)
    {
        _previous = UnityEngine.Random.state;
        UnityEngine.Random.InitState(seed);
        ItemSerialGenerator.Reset();
    }

    /// <summary>
    /// Restores the random state captured when the scope was created.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UnityEngine.Random.state = _previous;
    }
}
