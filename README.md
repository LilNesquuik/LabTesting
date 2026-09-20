# LabTesting

Test LabAPI plugins inside a real SCP:SL server, driven by a standalone runner.
The harness runs inside the game on **net48**; `labtest` runs on **.NET 10** on
Windows and Linux x64.

A single `PackageReference` in the net48 test project:

```xml
<PackageReference Include="LabTesting" Version="0.3.0" PrivateAssets="all" />
```

```sh
dotnet build tests/MyPlugin.Tests -c Release -t:LabTesting
```

The NuGet restore installs the harness, Harmony, the runner and the MSBuild
targets at the same version. Prefer a plain CLI instead of MSBuild?
`dotnet tool install --global LabTesting.Tool`.

**Full documentation: [the wiki](wiki/Home.md).**

- [Getting Started](wiki/Getting-Started.md)
- [Writing Tests](wiki/Writing-Tests.md)
- [Configuration Reference](wiki/Configuration-Reference.md)
- [Continuous Integration](wiki/Continuous-Integration.md)
- [Troubleshooting](wiki/Troubleshooting.md)
- [Compatibility](wiki/Compatibility.md)
- [Minimal plugin](examples/SamplePlugin/Plugin.cs) and [its tests](examples/SamplePlugin.Tests/CounterTests.cs)

## Contributing to LabTesting itself

The steps above are for *using* the package in a plugin repository. Building
the framework's own sources (`src/LabTesting`, the harness) additionally
needs a local SCP:SL server, since the harness compiles against its
assemblies:

```powershell
$env:SL_REFERENCES = 'C:/path/to/server/SCPSL_Data/Managed'
$env:EXILED_REFERENCES = $env:SL_REFERENCES
dotnet build src/LabTesting -c Release
dotnet run --project src/LabTesting.Runner -c Release -- run --config examples/framework.json --server C:/path/to/server
```

This step only builds on Windows and only matters for the framework itself —
see [Compatibility](wiki/Compatibility.md) for why, and for the one case
where it can affect a consuming plugin too.

## License

[MIT](LICENSE). Redistributed third-party components are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
