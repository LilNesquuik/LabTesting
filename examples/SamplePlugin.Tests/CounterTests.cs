using System.Threading.Tasks;
using LabTesting;
using PlayerRoles;
using SamplePlugin;

namespace SamplePlugin.Tests;

[Collection("Counter")]
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
    [Fact]
    public void Increment_is_visible()
    {
        CounterPlugin.Increment();
        Assert.Equal(1, CounterPlugin.Count);
    }
    [Fact]
    public async Task State_remains_stable()
    {
        CounterPlugin.Increment();
        await Expect.Always(() => CounterPlugin.Count == 1, 3.Ticks());
    }
    [Fact]
    public async Task Dummy_is_ready()
    {
        var player = await World.Spawn(RoleTypeId.ClassD, "Example");
        Assert.True(player.Hub.IsDummy);
        await Swallowed.AssertNone();
    }
    [Fact]
    public async Task Role_change_raises_an_event()
    {
        var changed = Expect.Event<LabApi.Events.Arguments.PlayerEvents.PlayerChangedRoleEventArgs>(120.Ticks());
        await World.Spawn(RoleTypeId.ClassD, "Events");
        Assert.NotNull(await changed);
    }
}
