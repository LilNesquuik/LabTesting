# Writing Tests

A test is a public method tagged `[Fact]` or `[Theory]` on a class with a
parameterless constructor, living in a **net48** class library. Attributes
and assertions come from the `LabTesting` namespace — the names mirror
xUnit, but they aren't xUnit, so don't `using Xunit;`.

```csharp
using LabTesting;

public sealed class MyTests
{
    [Fact]
    public void Two_plus_two()
    {
        Assert.Equal(4, 2 + 2);
    }
}
```

## Synchronous assertions

`Assert.True`, `False`, `Equal<T>`, `NotNull`, `Throws<TEx>`. Each one takes
an optional `because` string shown in the failure report.

```csharp
Assert.True(player.IsDead, "the victim should have died from the hit");
Assert.Throws<InvalidOperationException>(() => Commands.Run("nonsense"));
```

## Waiting on the game

The server only moves forward one **tick** at a time (a `FixedUpdate`).
Never `Thread.Sleep` or spin a loop — yield instead, so the game keeps
ticking underneath your test:

```csharp
await Expect.Tick(30);                                   // just wait
await Expect.Eventually(() => player.IsDead, 60.Ticks());  // wait for a condition
await Expect.Always(() => player.Hub != null, 60.Ticks()); // must hold the whole time
await Expect.Never(() => player.IsDead, 60.Ticks());        // must never happen
```

`Expect.Frame(n)` waits frames instead of ticks — only needed for emulated
input, which lives for a single frame.

## Players

```csharp
[Fact]
public async Task Dummy_can_be_killed()
{
    var (attacker, victim) = await World.SpawnPair(RoleTypeId.NtfCaptain, RoleTypeId.ClassD);
    await attacker.Kill(victim);
    Assert.True(victim.IsDead);
}
```

- `World.Spawn(role, nick)`, `SpawnPair`, `SpawnMany(n, role)` create dummy
  players and wait until the server actually considers them ready.
- `player.Give(ItemType.Ammo9x19).Equip()` gives and equips an item.
- `player.Input.Click("Shoot")` drives the game's own input emulation
  instead of calling a server method directly — closer to what a real
  client does, but only works for actions the game currently exposes for
  that role.
- `player.Hub` and `player.Player` expose the raw game hub and the LabAPI
  wrapper for anything not covered above.

## Events

Subscribe **before** the action that raises the event — a raise that
happens before you start listening is lost:

```csharp
[Fact]
public async Task Role_change_raises_an_event()
{
    var changed = Expect.Event<PlayerChangedRoleEventArgs>(120.Ticks());
    var player = await World.Spawn(RoleTypeId.ClassD, "Test");
    Assert.NotNull(await changed);
}
```

To watch several events over a longer stretch, use a recorder:

```csharp
using var events = Expect.Events<PlayerDeathEventArgs, PlayerSpawnedEventArgs>();
await attacker.Kill(victim);
Assert.Equal(1, events.CountOf<PlayerDeathEventArgs>());
events.AssertOrdered(); // death, then respawn, in that order
```

Not every event LabAPI declares is ever raised by the game (a few ammo/armor
search and pickup lifecycle events, for instance) — a test waiting on one of
those will always time out.

## Commands

```csharp
[Fact]
public void Ban_command_requires_permission()
{
    var player = await World.Spawn(RoleTypeId.ClassD, "NoPerms");
    CommandResult result = Commands.RunAs(player, "ban", "2", "reason");
    Assert.False(result.Success);
}
```

`Commands.Run` executes as the server (full permissions); `Commands.RunAs`
executes as a given player, using that player's own permissions — a fresh
dummy has none, which is exactly what you want to test a refusal.

## Cleaning up static state

The harness resets the round and the dummies it created; it knows nothing
about your plugin's own static fields or dictionaries. Restore them
yourself with `IAsyncLifetime`:

```csharp
public sealed class CounterTests : IAsyncLifetime
{
    private int previous;

    public Task InitializeAsync()
    {
        previous = CounterPlugin.Count;
        CounterPlugin.Count = 0;
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        CounterPlugin.Count = previous;
        return Task.CompletedTask;
    }
}
```

`DisposeAsync` still runs after a failing test. A fresh fixture instance is
created for every test method — state doesn't leak between them through the
fixture itself, only through anything static.

## Isolation: how much mess a test may leave behind

```csharp
[Fact]
[Isolation(Dirty.Round)]
public async Task Round_ending_awards_points() { /* ... */ }
```

| Level | Meaning | Cleanup |
|---|---|---|
| `Dirty.Dummies` (default) | Only creates dummy players | Destroys them |
| `Dirty.Round` | Changes round state | Also restarts the round |
| `Dirty.Process` | Needs a server to itself | Currently degrades to `Round` — true per-test isolation isn't implemented yet; split such tests into separate `labtest run --test` invocations |

If a test leaves behind more than it declared, the runner detects the drift
and raises the isolation level for the *next* test — but the offending test
still shows up as passed. Declare the real level rather than relying on
this safety net.

## Data-driven tests

```csharp
[Theory]
[InlineData(RoleTypeId.ClassD)]
[InlineData(RoleTypeId.Scientist)]
public async Task Every_civilian_role_spawns_unarmed(RoleTypeId role)
{
    var player = await World.Spawn(role, "Test");
    Assert.Equal(0, player.Player.Items.Count);
}
```

Each `[InlineData]` produces one test case, numbered `(0)`, `(1)`, ... in
reports and filters.

## Grouping and tagging

```csharp
[Collection("Inventory")]
[Trait("area", "inventory")]
public sealed class GiveTests { /* ... */ }
```

- `[Collection]` keeps related tests scheduled together and names the group
  in reports.
- `[Trait("name", "value")]` on a class or a method tags tests for filtering
  (`labtest run --trait area=inventory`) — see [Configuration Reference](Configuration-Reference).

## Timeouts and determinism

```csharp
[Fact]
[Timeout(1200)]   // ticks, not seconds — defaults to 600 (10s at 60 Hz)
[Seed(42)]        // makes UnityEngine.Random reproducible for this test
public async Task Long_running_scenario() { /* ... */ }
```

`[Seed]` only covers what the game itself seeds from `UnityEngine.Random`.
Wave composition, keycard words and role-assignment order stay
non-deterministic by design — assert on invariants ("every wave has at
least one class D"), not on exact values.

## Swallowed exceptions

LabAPI catches exceptions thrown inside event handlers and only logs them —
a test would otherwise pass while a handler silently failed:

```csharp
await World.Spawn(RoleTypeId.ClassD, "Test");
await Swallowed.AssertNone();
```

## Performance tests

```csharp
[Fact]
[Perf]
public void Dispatch_is_cheap() { /* measure something */ }
```

`[Perf]` detaches the harness's own event listener for the duration of the
test, so the measurement isn't skewed by the harness observing it. Event
assertions (`Expect.Event`, `Expect.Events`) are rejected inside a `[Perf]`
test for the same reason.
