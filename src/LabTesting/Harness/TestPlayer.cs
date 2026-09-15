using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LabApi.Features.Wrappers;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using UnityEngine;

namespace LabTesting;

/// <summary>
/// A dummy player driven by a test.
/// </summary>
/// <remarks>
/// Anything LabAPI's Player wrapper already does well, such as roles, position, inventory and
/// damage, is delegated to it rather than reimplemented against the game types. What this class
/// adds is what the wrapper cannot provide: the recording connection, the real input path, and
/// waiting for the server to actually apply a change.
/// </remarks>
public sealed class TestPlayer
{
    private ushort _lastGivenSerial;

    internal TestPlayer(ReferenceHub hub, RecordingDummyConnection connection)
    {
        Hub = hub;
        Net = connection;
        Input = new DummyInput(hub);
    }

    /// <summary>
    /// Gets the underlying reference hub, for anything the wrappers do not expose.
    /// </summary>
    public ReferenceHub Hub { get; }

    /// <summary>
    /// Gets the LabAPI wrapper for the same player.
    /// </summary>
    /// <exception cref="InvalidOperationException">The hub no longer has a wrapper.</exception>
    public Player Player => Player.Get(Hub)
        ?? throw new InvalidOperationException("Le ReferenceHub n'a plus de wrapper LabAPI.");

    /// <summary>
    /// Gets a value indicating whether the player is dead.
    /// </summary>
    public bool IsDead => !Player.IsAlive;

    /// <summary>
    /// Gets what the server sent to this dummy. See <see cref="INetProbe"/> for what is and is not
    /// observable.
    /// </summary>
    public INetProbe Net { get; }

    /// <summary>
    /// Gets the real input path, which drives the player through the game's dummy action emulator
    /// rather than through server-side API calls.
    /// </summary>
    public IDummyInput Input { get; }

    /// <summary>
    /// Assigns a role and waits until the server has actually applied it.
    /// </summary>
    public async Task SetRole(RoleTypeId role)
    {
        Player.SetRole(role);
        await Expect.Eventually(
            () => Hub != null && Hub.roleManager.CurrentRole != null && Hub.roleManager.CurrentRole.RoleTypeId == role,
            120.Ticks(),
            "le rôle " + role + " doit être réellement assigné");
    }

    /// <summary>
    /// Adds an item to the inventory and remembers it, so that <see cref="Equip"/> can select it.
    /// </summary>
    /// <returns>This instance, so calls can be chained.</returns>
    public TestPlayer Give(ItemType item)
    {
        Item? added = Player.AddItem(item);
        if (added != null)
            _lastGivenSerial = added.Serial;
        return this;
    }

    /// <summary>
    /// Puts the item added by the last <see cref="Give"/> call in the player's hands.
    /// </summary>
    /// <returns>This instance, so calls can be chained.</returns>
    public TestPlayer Equip()
    {
        if (_lastGivenSerial != 0)
            Hub.inventory.ServerSelectItem(_lastGivenSerial);
        return this;
    }

    /// <summary>
    /// Moves this player in front of another one, along that player's line of sight.
    /// </summary>
    /// <param name="other">Player to stand in front of.</param>
    /// <param name="meters">Distance in meters.</param>
    public void TeleportInFrontOf(TestPlayer other, float meters)
    {
        Transform camera = other.Hub.PlayerCameraReference;
        Vector3 forward = camera != null ? camera.forward : Vector3.forward;
        Player.Position = other.Player.Position + forward.normalized * meters;
    }

    /// <summary>
    /// Kills another player through the server damage path, and waits until they are actually
    /// dead.
    /// </summary>
    /// <remarks>
    /// This is the direct path, which is the framework's default. Going through
    /// <see cref="Input"/> is more faithful but depends on the action being exposed by the game
    /// for the current role.
    /// </remarks>
    public async Task Kill(TestPlayer victim)
    {
        victim.Player.Damage(float.MaxValue, Player);
        await Expect.Eventually(() => victim.IsDead, 60.Ticks(), victim.Player.Nickname + " doit mourir");
    }
}

/// <summary>
/// Drives a dummy through the game's own input emulation.
/// </summary>
public interface IDummyInput
{
    /// <summary>
    /// Gets the action names the game currently exposes for this dummy.
    /// </summary>
    /// <remarks>
    /// The list is dynamic, so read it at the moment you need it rather than caching it. See
    /// <see cref="Click"/> for why an action can be missing without being unsupported.
    /// </remarks>
    IReadOnlyList<string> Names { get; }

    /// <summary>
    /// Presses and releases an action, then yields one frame.
    /// </summary>
    /// <param name="action">Action name without the suffix, for example <c>Shoot</c>.</param>
    /// <remarks>
    /// An emulated click only lives for a single frame (DummyKeyEmulator.cs:75-93), so the frame
    /// is yielded for you.
    /// <para>
    /// An action only appears once the game has polled it at least once, because the emulator
    /// registers listeners lazily (DummyKeyEmulator.cs:32-48). A missing action is therefore not
    /// necessarily unsupported: it may just not have been polled yet by the current role.
    /// </para>
    /// </remarks>
    Task Click(string action);

    /// <summary>
    /// Holds an action until a condition becomes true, then releases it.
    /// </summary>
    /// <param name="action">Action name without the suffix.</param>
    /// <param name="until">Condition sampled once per tick.</param>
    /// <param name="max">How long to hold before giving up and failing the test.</param>
    /// <remarks>The action is released even when the condition is never met.</remarks>
    Task Hold(string action, Func<bool> until, Ticks max);

    /// <summary>
    /// Releases a held action, then yields one frame.
    /// </summary>
    Task Release(string action);
}

/// <summary>
/// Input implementation backed by the game's dummy action collector.
/// </summary>
/// <remarks>
/// Actions are exposed under the names the game builds, which are the action followed by
/// <c>-&gt;Click</c>, <c>-&gt;Hold</c> or <c>-&gt;Release</c> (DummyKeyEmulator.cs:50-73). Hold and
/// Release are mutually exclusive: only one of the two is offered at a time, depending on whether
/// the action is currently held, so the list is re-read before every call.
/// </remarks>
internal sealed class DummyInput : IDummyInput
{
    private readonly ReferenceHub _hub;

    public DummyInput(ReferenceHub hub) => _hub = hub;

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The hub is not a dummy.</exception>
    public IReadOnlyList<string> Names
    {
        get
        {
            List<DummyAction> actions = DummyActionCollector.ServerGetActions(_hub);
            var names = new string[actions.Count];
            for (int i = 0; i < actions.Count; i++)
                names[i] = actions[i].Name;
            return names;
        }
    }

    /// <inheritdoc/>
    public async Task Click(string action)
    {
        Invoke(action + "->Click");
        await Expect.Frame();
    }

    /// <inheritdoc/>
    public async Task Hold(string action, Func<bool> until, Ticks max)
    {
        Invoke(action + "->Hold");
        try
        {
            long deadline = Pump.Tick + max.Count;
            bool satisfied = false;
            await Pump.WaitTick(() =>
            {
                try
                {
                    satisfied = until();
                }
                catch
                {
                    satisfied = false;
                }

                return satisfied || Pump.Tick >= deadline;
            });

            if (!satisfied)
                TestContext.Fail("Input.Hold(" + action + ")", "condition atteinte sous " + max, "non atteinte", null);
        }
        finally
        {
            TryInvoke(action + "->Release");
        }
    }

    /// <inheritdoc/>
    public async Task Release(string action)
    {
        Invoke(action + "->Release");
        await Expect.Frame();
    }

    private void Invoke(string name)
    {
        if (TryInvoke(name))
            return;

        TestContext.Fail(
            "Input",
            "action « " + name + " » exposée",
            "actions disponibles : " + string.Join(", ", Names),
            "une action n'apparaît qu'après avoir été sondée par le jeu (DummyKeyEmulator.cs:32-48)");
    }

    private bool TryInvoke(string name)
    {
        List<DummyAction> actions = DummyActionCollector.ServerGetActions(_hub);
        for (int i = 0; i < actions.Count; i++)
        {
            if (!string.Equals(actions[i].Name, name, StringComparison.Ordinal))
                continue;

            actions[i].Action?.Invoke();
            return true;
        }

        return false;
    }
}
