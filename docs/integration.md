# Integrating a plugin

## 1. Prerequisites and local install

Install the .NET 10 SDK to build and to run the runner. The server uses its own
Mono for the net48 harness. The NuGet package ships a portable runner that relies
on that SDK.

**Windows**: install "SCP Secret Laboratory Dedicated Server" through Steam, or
extract SteamCMD and run:

```powershell
./steamcmd.exe +login anonymous +force_install_dir C:/scpsl +app_update 996560 validate +quit
```

Install the server's native prerequisites, notably the Visual C++ Redistributable.
Paths with spaces are fine.

**Ubuntu x64**: the template uses Ubuntu 24.04. Install the dependencies then use
the shipped script:

```bash
sudo apt-get update
sudo apt-get install -y lib32gcc-s1 lib32stdc++6 libatomic1 libgomp1 libglu1-mesa libxcursor1 libxrandr2 libxi6 libasound2t64 util-linux
bash scripts/install-server.sh "$PWD/.server" "$HOME/steamcmd-labtesting"
```

`install-server.sh` lives in the repository and ships inside the NuGet package
(`tools/server/`). A freshly downloaded SteamCMD updates itself on its first run
and can fail `app_update` with "Missing configuration"; the script warms it up
and retries.

Anonymous SteamCMD downloads use application **996560**. The server and its
isolated copy take several GB each: plan the space for every parallel worker. See
the current prerequisites for the
[dedicated server](https://techwiki.scpslgame.com/books/server-guides/page/1-how-to-create-a-dedicated-server).

## 2. Starting from an empty repository

1. Create the plugin repository.
2. Copy the `examples/SamplePlugin` and `examples/SamplePlugin.Tests` directories,
   plus `examples/labtesting.json` and `global.json`.
3. Add the package to the net48 test project, with a **pinned published version**:

   ```xml
   <PackageReference Include="LabTesting" Version="0.3.0" PrivateAssets="all" />
   ```

4. Nothing to configure if the dedicated server is already installed through
   Steam: the runner auto-detects it (app 996560, default install location).
   Otherwise install it into `.server/`, or pass its path through
   `-p:LabTestingServer=`. CI has no Steam install, so it always installs its
   own copy and passes that path explicitly (see [github-actions.md](github-actions.md)).
5. Build and run:

   ```bash
   export SL_REFERENCES="$PWD/.server/SCPSL_Data/Managed"
   export EXILED_REFERENCES="$SL_REFERENCES"
   dotnet build tests/MyPlugin.Tests -c Release -t:LabTestingList
   dotnet build tests/MyPlugin.Tests -c Release -t:LabTesting
   ```

Under PowerShell, the same commands with `$env:SL_REFERENCES`. A plain build
restores and compiles without starting the server; only the `LabTesting` and
`LabTestingList` targets start it.
[examples/NuGetPlugin.Tests](../examples/NuGetPlugin.Tests) is a complete consumer
of the package: csproj, `labtesting.json` and tests shared with
`SamplePlugin.Tests`. The available properties are listed in
[packaging/README.md](../packaging/README.md).

`SamplePlugin.Tests` is a net48 library, not a `dotnet test` project. It
references LabTesting and the plugin. The attributes come from `LabTesting`, not
from xUnit. Adapt names, paths and assertions to your plugin.

## 3. Writing tests

The [full project](../examples/SamplePlugin.Tests/CounterTests.cs) holds four
runnable tests: a synchronous one, an asynchronous invariant, a dummy player and
an event.

```csharp
[Fact]
public void Increment_is_visible()
{
    CounterPlugin.Increment();
    Assert.Equal(1, CounterPlugin.Count);
}

[Fact]
public async Task Dummy_is_ready()
{
    var player = await World.Spawn(RoleTypeId.ClassD, "Test");
    Assert.True(player.Hub.IsDummy);
    await Expect.Eventually(() => player.Hub.IsDummy, 60.Ticks());
    await Swallowed.AssertNone();
}
```

Waits are expressed in game ticks. Avoid `Thread.Sleep` and loops that block the
Unity thread. `Expect.Tick`, `Frame`, `Eventually`, `Always` and `Never` let the
game move forward. `[Timeout(600)]` bounds the test body in ticks; the runner's
timeout bounds the whole run, including startup and stuck fixtures.

Subscribe **before** the action that raises the event:

```csharp
var changed = Expect.Event<PlayerChangedRoleEventArgs>(120.Ticks());
await World.Spawn(RoleTypeId.ClassD, "Event");
Assert.NotNull(await changed);
```

Import `LabApi.Events.Arguments.PlayerEvents`. Use `Expect.Events<T1,T2>()` inside
a `using` to observe a sequence. Some catalogued events are never raised
naturally by the game: see `EventCatalog.NeverRaisedByGame`.

Assertions, ordinary exceptions, exceptions swallowed by LabAPI and teardown
errors all shape the result. Details and stack traces are kept in the JSONL and in
the JUnit report.

## 4. Cleaning static registries

The world cleanup knows nothing about your plugin's dictionaries. Implement
`IAsyncLifetime`, save the state before the test, restore it afterwards,
unsubscribe from events and cancel the tasks you started:

```csharp
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
```

Fixtures are recreated for every case. `DisposeAsync` is attempted even after a
setup failure. Its errors fail the test. Tests within a suite run sequentially; a
collection is a selection group, not a shared fixture.

`Dirty.Dummies` clears the dummy players; `Dirty.Round` restarts the round.
**`Dirty.Process` currently degrades to Round**, it is not a fresh process per
test. To isolate a registry you cannot restore, run separate suites with `--test`
and one server process per suite.

## 5. Dependencies and data

Declare the plugin under test **and every required plugin** under `plugins`. The
harness checks they are enabled. Libraries without a Plugin class go under
`dependencies`, `0Harmony.dll` for instance. Tests go under `tests` and are loaded
explicitly after LabAPI has loaded.

A DLL must appear only once, even when several plugins depend on it. Do not copy
the whole `bin/` directory: it may hold game, Unity or LabAPI assemblies. The
server supplies those.

```json
{
  "files": [
    { "source": "fixtures/config.yml", "root": "labapiConfig", "target": "MyPlugin/config.yml" },
    { "source": "fixtures/scenario.json", "root": "data", "target": "scenario.json" }
  ],
  "serverSettings": { "max_players": "8" }
}
```

Match the configuration subdirectory to the name the plugin actually uses. The
data is reachable through `Path.Combine(Environment.CurrentDirectory,
"test-data", "scenario.json")`. A configuration pointing at a network service or
an external absolute path stays the plugin's own responsibility.

## 6. Troubleshooting

| Symptom | What to check |
|---|---|
| Missing DLL / ReflectionTypeLoadException | Read the LoaderExceptions in the JSONL or JUnit. Add the DLL to dependencies, or the required plugin to plugins. Check the versions; do not copy game assemblies from another server. |
| No test discovered | Use list, check tests, the LabTesting.Fact/Theory attributes, the exact filters and the net48 target. The framework tests do not make up for an empty external assembly. |
| Port in use | Pick another --port. The LabTesting lock and the UDP/TCP binds reject collisions. |
| Server stuck | Read stdout.log and stderr.log, check the native install and use --keep to inspect the copy. A timeout produces a failure with the reports kept. |
| Missing verdict | The harness could not arm: look for a load error, a dependency, the LabAPI version or a refused sentinel. |
| Summary present but the workflow is red | Check the server exit code, missing results, duplicates, teardown errors and fail-on-skipped. A summary alone does not authorize success. |
| Permission denied on Linux | Check the rights on the source server's executable. The runner preserves Unix modes when copying. |
| Cleanup refused on Windows | A process or an antivirus holds a file open. The reports name the directory kept; check the processes before deleting only that labtest copy. |
| The plugin still reads a personal file | Check that it uses the standard LabAPI paths. Absolute paths hardcoded in the plugin cannot be redirected automatically. |
| CS7069, or CS0122 on a Harmony-internal type, only in Linux CI | A private dependency was built against the game's assemblies and forces a `HintPath` to its `mscorlib`/`netstandard`/`System`. The Linux SDK's own targeting-pack `mscorlib` wins over that `HintPath`. Build that job on `windows-latest` instead; see [github-actions.md](github-actions.md). |
