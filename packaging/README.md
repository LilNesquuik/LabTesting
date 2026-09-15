# LabTesting

One package to test a LabAPI plugin inside a real SCP:SL server, on Windows and
Linux. It carries the net48 harness, Harmony 2.3.6, the portable .NET 10 runner
and the MSBuild targets. Install the .NET 10 SDK and the SteamCMD dedicated
server 996560 separately.

In the net48 test project:

```xml
<PackageReference Include="LabTesting" Version="0.1.1" PrivateAssets="all" />
```

Pin the published version you chose. The NuGet restore installs the whole
framework and keeps harness and runner at the same version. No EXE to copy, no
dotnet-tools.json to maintain.

Put labtesting.json at the repository root, or next to the test project:

```json
{
  "server": ".server",
  "plugins": ["src/MyPlugin/bin/Release/net48/MyPlugin.dll"],
  "dependencies": [],
  "reports": "TestResults",
  "work": ".labtest-work",
  "port": 7799
}
```

The package supplies the harness, Harmony and the test project's own assembly.
Do not redeclare them in the JSON. Required plugins and their other dependencies
stay explicit. JSON paths are relative to the JSON file.

```sh
dotnet build tests/MyPlugin.Tests -c Release
dotnet build tests/MyPlugin.Tests -c Release -t:LabTestingList
dotnet build tests/MyPlugin.Tests -c Release -t:LabTesting
```

A plain build restores and compiles; the server tests only start on demand. To
run them after every build, add
`<LabTestingRunOnBuild>true</LabTestingRunOnBuild>` to the test project.

Optional properties: LabTestingConfig (relative to the project), LabTestingServer,
LabTestingReports, LabTestingWork, LabTestingPort, LabTestingTimeout.
LabTestingFailOnSkipped defaults to true. CLI and MSBuild path parameters are
relative to the test project; prefer absolute paths in CI.

Assertions and attributes live in the LabTesting namespace. Fixtures may
implement IAsyncLifetime to restore their static state. The JSONL, JUnit and
Markdown reports and the logs stay available after a failure. An empty suite or
an incomplete verdict fails the build.

The package includes no game, Unity or LabAPI assembly. Add the plugin and server
references matching the types your tests use. PrivateAssets="all" keeps this test
framework from flowing into your own packages.

`lib/net48` also carries `LabTesting.pdb` with SourceLink data, so a stack trace
inside the harness resolves to file and line on GitHub.
