# LabTesting

Test LabAPI plugins inside a real SCP:SL server, driven by a standalone runner.
The harness runs inside the game on **net48**; `labtest` runs on **.NET 10** on
Windows and Linux x64.

A single `PackageReference` in the net48 test project:

```xml
<PackageReference Include="LabTesting" Version="0.1.1" PrivateAssets="all" />
```

```sh
dotnet build tests/MyPlugin.Tests -c Release -t:LabTesting
```

The NuGet restore installs the harness, Harmony, the runner and the MSBuild
targets at the same version. Standalone archives remain available for an
installation without NuGet: `.labtesting/runner/labtest run --config examples/labtesting.json`.

- [Integrating from an empty repository](docs/integration.md)
- [Configuration, isolation and protocol](docs/runner.md)
- [GitHub Actions and private dependencies](docs/github-actions.md)
- [Distributions and compatibility](docs/releases.md)
- [Validation record and known limits](docs/validation.md)
- [Minimal plugin](examples/SamplePlugin/Plugin.cs) and [its tests](examples/SamplePlugin.Tests/CounterTests.cs)

## From source

Install the .NET 10 SDK and the Steam dedicated server 996560, then:

```powershell
$env:SL_REFERENCES = 'C:/path/to/server/SCPSL_Data/Managed'
$env:EXILED_REFERENCES = $env:SL_REFERENCES
dotnet build src/LabTesting -c Release
dotnet run --project src/LabTesting.Runner -c Release -- --selftest
dotnet run --project src/LabTesting.Runner -c Release -- run --config examples/framework.json --server C:/path/to/server
```

The framework's own tests require `frameworkTests: true`. An empty external suite
fails even when the framework tests are enabled. Reports stay in
`TestResults/<unique-id>/`, including after an error.

The net48 assemblies only build on Windows: the game's `Managed` directory ships
none of the .NET Framework facades, and a Linux machine has no targeting pack to
fall back on. Those Windows-built DLLs are the ones executed on Linux, which is
where every production SCP:SL server runs. The constraint is specific to this
harness — an ordinary net48 plugin test project builds fine on Linux.

**Status**: builds, standalone archives and real runs on Windows and Ubuntu 24.04
are validated — 4 sample plugin tests and 12 framework tests on each system. The
NuGet package is validated on both systems from a clean cache (restore, build,
discovery and 4/4 real tests), locally and on GitHub runners. See the detailed
record.

## License

[MIT](LICENSE). Redistributed third-party components are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
