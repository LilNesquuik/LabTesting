using System.Threading.Tasks;
using PlayerRoles;

namespace LabTesting.Tests;

/// <summary>
/// Checks that the clock works: yielding ticks and frames, and the three timing assertions.
/// </summary>
/// <remarks>
/// These tests need no third-party plugin. If they pass on a real server, the pump and the
/// synchronization context behave as intended. They also serve as the shortest example of the
/// syntax.
/// </remarks>
[Collection("Smoke")]
public sealed class PumpTests
{
    [Fact]
    public async Task Yielding_ticks_advances_the_counter()
    {
        long before = Pump.Tick;
        await Expect.Tick(3);
        Assert.True(Pump.Tick >= before + 3, "yielding 3 ticks must advance the counter by at least 3");
    }

    [Fact]
    public async Task Yielding_frames_advances_the_counter()
    {
        long before = Pump.Frame;
        await Expect.Frame(2);
        Assert.True(Pump.Frame >= before + 2, "yielding 2 frames must advance the counter by at least 2");
    }

    [Fact]
    public async Task Eventually_succeeds_once_the_condition_turns_true()
    {
        long target = Pump.Tick + 5;
        await Expect.Eventually(() => Pump.Tick >= target, 60.Ticks(), "the target tick must be reached");
    }

    [Fact]
    public async Task Always_holds_on_a_stable_invariant()
    {
        await Expect.Always(() => Pump.Installed, 10.Ticks(), "the pump stays installed");
    }

    [Fact]
    public async Task Never_holds_when_nothing_happens()
    {
        await Expect.Never(() => Pump.Tick < 0, 10.Ticks(), "the tick counter never goes negative");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Theory_receives_its_arguments(int ticks)
    {
        long before = Pump.Tick;
        await Expect.Tick(ticks);
        Assert.True(Pump.Tick >= before + ticks, "yields " + ticks + " ticks");
    }
}

/// <summary>
/// Checks player creation: readiness, role assignment, and per-player network probes.
/// </summary>
[Collection("Smoke")]
public sealed class WorldTests
{
    [Fact, Isolation(Dirty.Dummies), Seed(1337)]
    public async Task A_spawned_dummy_is_ready_with_its_role()
    {
        TestPlayer player = await World.Spawn(RoleTypeId.ClassD, "Smoke");

        Assert.True(player.Hub.IsDummy, "the hub must be in ClientInstanceMode.Dummy");
        Assert.Equal(RoleTypeId.ClassD, player.Hub.roleManager.CurrentRole.RoleTypeId);
        await Swallowed.AssertNone();
    }

    /// <summary>
    /// Verifies that the spawn burst really does go through the overridden send of a recording
    /// dummy connection.
    /// </summary>
    /// <remarks>
    /// This is the experimental check behind the whole network observation feature. If it fails,
    /// <see cref="INetProbe"/> observes nothing and every network assertion is meaningless.
    /// </remarks>
    [Fact, Isolation(Dirty.Dummies)]
    public async Task The_dummy_receives_its_spawn_burst()
    {
        TestPlayer player = await World.Spawn(RoleTypeId.Spectator, "Probe");

        await player.Net.ExpectAnySend(120.Ticks());
        Assert.True(player.Net.SentCount > 0, "the recording connection must have captured bytes");
    }

    [Fact, Isolation(Dirty.Dummies)]
    public async Task Two_dummies_have_distinct_connections()
    {
        var pair = await World.SpawnPair(RoleTypeId.ClassD, RoleTypeId.Scientist);

        Assert.True(!ReferenceEquals(pair.Item1.Net, pair.Item2.Net), "one probe per dummy");
        Assert.True(pair.Item1.Hub != pair.Item2.Hub, "two distinct hubs");
        await Expect.Tick();
    }
}

/// <summary>
/// Checks command driving and the event catalogue.
/// </summary>
[Collection("Smoke")]
public sealed class CommandTests
{
    /// <summary>
    /// Checks that an unknown command is reported as an unsuccessful result rather than throwing.
    /// </summary>
    [Fact]
    public async Task An_unknown_command_is_reported_not_thrown()
    {
        CommandResult result = Commands.Run("commande_qui_nexiste_pas_" + Pump.Tick);

        Assert.False(result.Success, "an unknown command does not succeed");
        Assert.True(result.Response.Length > 0, "and it explains why");
        await Expect.Tick();
    }

    [Fact]
    public async Task The_event_catalogue_is_populated()
    {
        // The exact count is tied to the LabAPI version, so only a plausible lower bound is
        // asserted. Version 1.1.7 declares 370 typed events.
        Assert.True(EventCatalog.Count > 300, "the catalogue must hold the LabAPI 1.1.x events");
        await Expect.Tick();
    }
}
