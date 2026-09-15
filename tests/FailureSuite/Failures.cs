using System;
using System.Threading.Tasks;
using LabTesting;

namespace FailureSuite;

// Intentionally red suite: the CI verifies the precise outcomes, not a zero exit code.
[Collection("Failures")]
public sealed class BodyFailures
{
    [Fact]
    public void Assertion() => Assert.Equal(1, 2);
    [Fact]
    public void Exception() => throw new InvalidOperationException("intentional body exception");
    [Fact(Skip = "intentional skip")]
    public void Skipped() => throw new InvalidOperationException("must not run");
    [Fact]
    public async Task Swallowed()
    {
        LabApi.Features.Console.Logger.Error("intentional swallowed error");
        await Expect.Frame(2);
    }
}

[Collection("Failures")]
public sealed class SetupFailure : IAsyncLifetime
{
    public Task InitializeAsync() => throw new InvalidOperationException("intentional setup exception");
    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public void Body() => throw new InvalidOperationException("must not run after failed setup");
}

[Collection("Failures")]
public sealed class TeardownFailure : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => throw new InvalidOperationException("intentional teardown exception");
    [Fact]
    public void Body() { }
}

[Collection("Failures")]
public sealed class DisposeFailure : IDisposable
{
    public void Dispose() => throw new InvalidOperationException("intentional dispose exception");
    [Fact]
    public void Body() { }
}
