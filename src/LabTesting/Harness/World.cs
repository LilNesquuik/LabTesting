using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using RoundRestarting;
using UnityEngine;

namespace LabTesting;

/// <summary>
/// A cheap snapshot of server state, taken before and after each test to detect what a test left
/// behind.
/// </summary>
public readonly struct StateFingerprint
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StateFingerprint"/> structure.
    /// </summary>
    public StateFingerprint(int hubs, int spawned, int uptimeRounds)
    {
        Hubs = hubs;
        Spawned = spawned;
        UptimeRounds = uptimeRounds;
    }

    /// <summary>
    /// Gets the number of reference hubs, that is connected players and dummies plus the host.
    /// </summary>
    public int Hubs { get; }

    /// <summary>
    /// Gets the number of network identities currently spawned.
    /// </summary>
    public int Spawned { get; }

    /// <summary>
    /// Gets the number of rounds the server has completed since it started.
    /// </summary>
    public int UptimeRounds { get; }

    /// <summary>
    /// Takes a snapshot of the current server state.
    /// </summary>
    public static StateFingerprint Take() =>
        new(ReferenceHub.AllHubs.Count, NetworkServer.spawned.Count, RoundRestart.UptimeRounds);

    /// <summary>
    /// Returns whether this snapshot shows state the baseline did not have.
    /// </summary>
    /// <remarks>
    /// Spawned objects are only compared upwards: a round restart legitimately removes many of
    /// them, and that is not a leak.
    /// </remarks>
    public bool DriftedFrom(StateFingerprint baseline) =>
        Hubs != baseline.Hubs || Spawned > baseline.Spawned;

    /// <inheritdoc/>
    public override string ToString() =>
        "hubs=" + Hubs + " spawned=" + Spawned + " round=" + UptimeRounds;
}

/// <summary>
/// The test world: creating players, driving the round, and measuring server state.
/// </summary>
public static class World
{
    /// <summary>
    /// Creates a dummy player, waits until the server considers it ready, and optionally assigns
    /// it a role.
    /// </summary>
    /// <param name="role">Role to assign, or <see cref="RoleTypeId.None"/> to leave it alone.</param>
    /// <param name="nick">Nickname given to the dummy.</param>
    /// <returns>The ready-to-use test player.</returns>
    public static async Task<TestPlayer> Spawn(RoleTypeId role, string nick = "Dummy")
    {
        GameObject go = UnityEngine.Object.Instantiate(NetworkManager.singleton.playerPrefab);
        if (!go.TryGetComponent(out ReferenceHub hub))
        {
            UnityEngine.Object.Destroy(go);
            throw new InvalidOperationException("The playerPrefab has no ReferenceHub.");
        }

        hub.nicknameSync.MyNick = nick;
        RecordingDummyConnection connection = new RecordingDummyConnection();
        NetworkServer.AddPlayerForConnection(connection, go);

        // Unity overloads ==: a destroyed hub compares equal to null while the reference is not.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        await Expect.Eventually(() => hub != null && hub.IsDummy, 120.Ticks(),
            "the dummy must reach ClientInstanceMode.Dummy");

        TestPlayer player = new TestPlayer(hub, connection);
        Spawned.Add(player);

        if (role != RoleTypeId.None)
            await player.SetRole(role);

        return player;
    }

    /// <summary>
    /// Creates two dummies with the given roles.
    /// </summary>
    public static async Task<Tuple<TestPlayer, TestPlayer>> SpawnPair(RoleTypeId a, RoleTypeId b)
    {
        TestPlayer first = await Spawn(a, "DummyA");
        TestPlayer second = await Spawn(b, "DummyB");
        return Tuple.Create(first, second);
    }

    /// <summary>
    /// Creates several dummies with the same role.
    /// </summary>
    public static async Task<IReadOnlyList<TestPlayer>> SpawnMany(int n, RoleTypeId role)
    {
        List<TestPlayer> players = new List<TestPlayer>(n);
        for (int i = 0; i < n; i++)
            players.Add(await Spawn(role, "Dummy" + i));

        return players;
    }

    internal static List<TestPlayer> Spawned { get; } = [];

    /// <summary>
    /// Gets the number of rounds the server has completed since it started.
    /// </summary>
    public static int UptimeRounds => RoundRestart.UptimeRounds;

    /// <summary>
    /// Takes a snapshot of the current server state.
    /// </summary>
    public static StateFingerprint Fingerprint() => StateFingerprint.Take();

    /// <summary>
    /// Restarts the round and returns once a new round is actually running.
    /// </summary>
    /// <param name="within">How long to wait. Defaults to 3600 ticks, one minute at 60 Hz.</param>
    /// <remarks>
    /// Completion is checked on two conditions rather than a fixed delay: the round counter must
    /// have advanced, and a round must have started.
    /// <para>
    /// On an empty server the second condition never arrives on its own: the round counter advances
    /// but no round starts, because the countdown waits for players. The harness therefore starts
    /// the new round explicitly, exactly as it does for the first one.
    /// </para>
    /// </remarks>
    public static async Task RestartRound(Ticks within = default)
    {
        int before = RoundRestart.UptimeRounds;
        bool started = false;
        Action handler = () => started = true;

        CharacterClassManager.ServerOnRoundStartTriggered += handler;
        try
        {
            RoundRestart.InitiateRoundRestart();

            await Expect.Eventually(
                () => RoundRestart.UptimeRounds > before,
                within.Count > 0 ? within : 3600.Ticks(),
                "D8 barrier: UptimeRounds must be incremented (RoundRestart.cs:163)");

            if (started)
                return;

            await Expect.Eventually(
                () => GameCore.RoundStart.singleton != null,
                600.Ticks(),
                "the new round scene must be loaded");

            await ForceRoundStart(within);
        }
        finally
        {
            CharacterClassManager.ServerOnRoundStartTriggered -= handler;
        }
    }

    /// <summary>
    /// Starts the round now, and returns once it is running. Does nothing if a round is already in
    /// progress.
    /// </summary>
    /// <param name="within">How long to wait. Defaults to 1800 ticks, thirty seconds at 60 Hz.</param>
    public static async Task ForceRoundStart(Ticks within = default)
    {
        if (RoundSummary.RoundInProgress())
            return;

        bool started = false;
        Action handler = () => started = true;

        CharacterClassManager.ServerOnRoundStartTriggered += handler;
        try
        {
            if (!CharacterClassManager.ForceRoundStart())
            {
                TestContext.Fail("World.ForceRoundStart", "request accepted",
                    "refused (server inactive or RoundStartingEventArgs.IsAllowed == false)", null);
            }

            await Expect.Eventually(
                () => started || RoundSummary.RoundInProgress(),
                within.Count > 0 ? within : 1800.Ticks(),
                "the round must start");
        }
        finally
        {
            CharacterClassManager.ServerOnRoundStartTriggered -= handler;
        }
    }
}

/// <summary>
/// One serialized message the server sent to a dummy.
/// </summary>
/// <remarks>
/// The bytes are copied on capture: the segment handed to Send points into a pooled network writer
/// that is returned to the pool as soon as the call completes.
/// </remarks>
public readonly struct CapturedMsg
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CapturedMsg"/> structure.
    /// </summary>
    public CapturedMsg(byte[] bytes, int channelId, long tick)
    {
        Bytes = bytes;
        ChannelId = channelId;
        Tick = tick;
    }

    /// <summary>
    /// Gets the copied message bytes, header included.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// Gets the Mirror channel the message was sent on.
    /// </summary>
    public int ChannelId { get; }

    /// <summary>
    /// Gets the tick at which the message was captured.
    /// </summary>
    public long Tick { get; }

    /// <summary>
    /// Gets the Mirror message identifier read from the header, or zero if the message is too
    /// short.
    /// </summary>
    /// <remarks>
    /// Mirror writes a two-byte identifier before the payload.
    /// </remarks>
    public ushort MessageId
    {
        get
        {
            if (Bytes.Length < NetworkMessages.IdSize)
                return 0;

            NetworkReader reader = new NetworkReader(new ArraySegment<byte>(Bytes));
            return NetworkMessages.UnpackId(reader, out ushort id) ? id : (ushort)0;
        }
    }
}

/// <summary>
/// What the server sent to one specific dummy.
/// </summary>
/// <remarks>
/// Coverage is partial.
/// <para>
/// Observable: messages sent directly to the connection, the spawn burst the dummy receives when it
/// is added, and broadcasts to the observers of an identity.
/// </para>
/// <para>
/// Not observable: SendToAll, SendToReady, time snapshots and the SyncVar deltas produced by the
/// per-frame broadcast. All of them iterate the server connection dictionary, which a dummy is
/// never part of, because adding a player for a connection does not register that connection.
/// </para>
/// </remarks>
public interface INetProbe
{
    /// <summary>
    /// Gets the number of messages captured so far.
    /// </summary>
    int SentCount { get; }

    /// <summary>
    /// Gets the captured messages, oldest first.
    /// </summary>
    IReadOnlyList<CapturedMsg> Sent { get; }

    /// <summary>
    /// Returns how many captured messages are of the given Mirror message type.
    /// </summary>
    int CountOf<T>() where T : struct, NetworkMessage;

    /// <summary>
    /// Waits until at least one further message is captured, and fails the test otherwise.
    /// </summary>
    Task ExpectAnySend(Ticks within);

    /// <summary>
    /// Waits until at least one further message of the given type is captured, and fails the test
    /// otherwise.
    /// </summary>
    Task Expect<T>(Ticks within) where T : struct, NetworkMessage;

    /// <summary>
    /// Discards everything captured so far.
    /// </summary>
    void Clear();
}

/// <summary>
/// A dummy connection that records what the server sends to it.
/// </summary>
public sealed class RecordingDummyConnection : DummyNetworkConnection, INetProbe
{
    private readonly List<CapturedMsg> _sent = [];

    /// <inheritdoc/>
    public int SentCount => _sent.Count;

    /// <inheritdoc/>
    public IReadOnlyList<CapturedMsg> Sent => _sent;

    /// <inheritdoc/>
    public override void Send(ArraySegment<byte> segment, int channelId = 0)
    {
        if (segment.Array == null || segment.Count == 0)
            return;

        byte[] bytes = new byte[segment.Count];
        Buffer.BlockCopy(segment.Array, segment.Offset, bytes, 0, segment.Count);
        _sent.Add(new CapturedMsg(bytes, channelId, Pump.Tick));
    }

    /// <inheritdoc/>
    public int CountOf<T>() where T : struct, NetworkMessage
    {
        ushort id = NetworkMessageId<T>.Id;
        int n = 0;
        for (int i = 0; i < _sent.Count; i++)
        {
            if (_sent[i].MessageId == id)
                n++;
        }

        return n;
    }

    /// <inheritdoc/>
    public async Task ExpectAnySend(Ticks within)
    {
        int before = _sent.Count;
        await LabTesting.Expect.Eventually(() => _sent.Count > before, within,
            "the dummy connection must receive at least one message");
    }

    /// <inheritdoc/>
    public async Task Expect<T>(Ticks within) where T : struct, NetworkMessage
    {
        int before = CountOf<T>();
        await LabTesting.Expect.Eventually(() => CountOf<T>() > before, within,
            typeof(T).Name + " message expected");
    }

    /// <inheritdoc/>
    public void Clear() => _sent.Clear();
}
