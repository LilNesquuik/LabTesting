# Getting Started

## 1. Install a dedicated server

Tests run against a real SCP:SL server, so one has to exist on the machine —
yours, or CI's.

- **Windows**: install "SCP Secret Laboratory Dedicated Server" through
  Steam, or run the install script from the package:
  ```powershell
  tools/server/install-server.ps1 -Server C:/scpsl -SteamCmd C:/steamcmd
  ```
- **Linux**: install the native prerequisites, then run the equivalent
  script:
  ```bash
  sudo apt-get install -y lib32gcc-s1 lib32stdc++6 libatomic1 libgomp1 libglu1-mesa libxcursor1 libxrandr2 libxi6 libasound2t64 util-linux
  tools/server/install-server.sh ~/scpsl ~/steamcmd
  ```

If Steam already has the server installed locally, LabTesting finds it on
its own — you only need to pass a path (`--server`, or `LabTestingServer`)
when there's more than one copy, or in CI, where nothing is installed yet.

## 2. Add the package to your test project

Tests live in a **net48 class library** — not a `dotnet test` project,
because that's what the game's own runtime (Mono) can load.

```xml
<PackageReference Include="LabTesting" Version="0.3.0" PrivateAssets="all" />
```

Restoring it brings the harness, Harmony, the runner and the MSBuild targets
that wire everything together — nothing else to install or copy by hand.

Prefer a plain CLI over MSBuild? Install the runner once as a global tool:

```sh
dotnet tool install --global LabTesting.Tool
labtest --selftest
```

## 3. Reference the game

Tests touch game types (`RoleTypeId`, `ReferenceHub`, ...), so the test
project references the server's own assemblies:

```xml
<ItemGroup>
  <Reference Include="Assembly-CSharp" HintPath="$(SL_REFERENCES)/Assembly-CSharp.dll" Private="false" />
  <Reference Include="Mirror" HintPath="$(SL_REFERENCES)/Mirror.dll" Private="false" />
  <Reference Include="UnityEngine.CoreModule" HintPath="$(SL_REFERENCES)/UnityEngine.CoreModule.dll" Private="false" />
</ItemGroup>
```

You don't have to set `SL_REFERENCES`: the package points it at
`<LabTestingServer>/SCPSL_Data/Managed` when you pass a server, and at the
default Steam install otherwise (`C:\Program Files (x86)\Steam\...` on
Windows, `~/.local/share/Steam/...` on Linux). For a server anywhere else, set
the `SL_REFERENCES` environment variable to its `SCPSL_Data/Managed` folder.
The CI templates already do.

## 4. Get a config file

```sh
dotnet build MyPlugin.Tests -c Release -t:LabTestingInit
```

This scaffolds `labtesting.json` and a GitHub Actions workflow at the
repository root, filling `plugins` from your test project's own
`ProjectReference` entries. It refuses to overwrite an existing file.

Or write `labtesting.json` by hand next to your test project (or at the
repository root):

```json
{
  "plugins": ["src/MyPlugin/bin/Release/net48/MyPlugin.dll"],
  "dependencies": []
}
```

Paths are relative to the JSON file. When you run through MSBuild, the
harness, Harmony and your own test assembly are added automatically — don't
list them yourself. Everything else is in the [Configuration Reference](Configuration-Reference).

## 5. Run it

```sh
dotnet build MyPlugin.Tests -c Release -t:LabTestingList   # discover, don't run
dotnet build MyPlugin.Tests -c Release -t:LabTesting        # run
```

With the global tool instead:

```sh
labtest list --config labtesting.json
labtest run  --config labtesting.json
```

A plain `dotnet build` never starts the server — only the two targets (or
the CLI) do.

## 6. Write your first test

```csharp
using LabTesting;

[Collection("Counter")]
public sealed class CounterTests
{
    [Fact]
    public void Increment_is_visible()
    {
        CounterPlugin.Increment();
        Assert.Equal(1, CounterPlugin.Count);
    }
}
```

`Assert` and `[Fact]` come from the `LabTesting` namespace, not xUnit — see
[Writing Tests](Writing-Tests) for the full picture, including waiting on
the game and spawning players. The
[sample plugin](https://github.com/LilNesquuik/LabTesting/blob/master/examples/SamplePlugin.Tests/CounterTests.cs)
is a complete, runnable reference.
