# Distributions and compatibility

## NuGet package

This is the way to install: a single `PackageReference` brings the net48 harness,
Harmony, the portable .NET 10 runner and the MSBuild targets.

```powershell
pwsh -File scripts/pack-nuget.ps1 -Version 0.3.0 -Managed C:/scpsl/SCPSL_Data/Managed
```

The script builds the harness, publishes the runner framework-dependent, runs
`--selftest`, writes `manifest.json` and then calls `dotnet pack`. The contents
are an allow-list, file by file: never a glob over a `bin` directory.
`scripts/verify-nuget.ps1` then checks what the archive holds, and the
`.nupkg.sha256` is written next to the package. The script refuses to overwrite an
existing package: pick a fresh `-Output`.

`scripts/test-nuget-consumer.ps1` validates the package the way a real consumer
would: clean NuGet cache, restore from a local directory, build
`examples/NuGetPlugin.Tests`, then `-t:LabTestingList` and `-t:LabTesting` with
`-RunServer` against a real server. No path to the LabTesting sources is used.

`lib/net48` also ships `LabTesting.pdb`. SourceLink data is embedded in it, so a
stack trace inside the harness resolves to file and line against the GitHub
sources of the matching commit.

The `.github/workflows/nuget-release.yml` workflow replays all of this whenever a
release is published, prereleases included: packaging on Windows, validation on
`windows-latest` and `ubuntu-24.04` against a real SteamCMD server, then
publication to NuGet.org and GitHub Packages, and the `.nupkg` and its SHA-256
attached to the release. A `workflow_dispatch` with `publish: false` runs the
validation without publishing anything.

Publishing to NuGet.org goes through **trusted publishing**: the job asks GitHub
for an OIDC token (`id-token: write`), `NuGet/login@v1` exchanges it for a
single-use key valid one hour, and the push uses it immediately. No long-lived API
key is stored. The only secret is **NUGET_USER**, the nuget.org profile name. The
matching policy on nuget.org targets the `LilNesquuik/LabTesting` repository, the
`nuget-release.yml` file, no environment, the Push scope and the `LabTesting`
pattern. The push to GitHub Packages keeps using the job's `GITHUB_TOKEN`.

`release.yml` runs the real-server validation on a `v<version>` tag push, then
creates a **draft** release for that tag with no attached assets; publishing that
draft triggers `nuget-release.yml`, which does the actual packaging.

## Compatibility matrix

| Component | Reference for this version |
|---|---|
| Runner | .NET 10, Windows/Linux x64 |
| Harness and tests | net48, loaded by the server's Mono |
| SCP:SL validated | 14.2.7 on Windows and Ubuntu 24.04 |
| LabAPI used to build and test | 1.1.7 |
| Harmony | 2.3.6 |

The minimum version the plugin declares is LabAPI 1.1.0, which is not a validation
of every 1.1.x release. The harness reaches into internal game details through
publicizing: rebuild and rerun the suite after every SCP:SL or LabAPI update. A
failure on a new version must not be worked around by accepting an incomplete
verdict.

.NET 10 is an LTS release, see the
[official .NET policy](https://dotnet.microsoft.com/en-us/platform/support/policy).
The isolation paths rely on the gamedir policy of
[LabAPI's PathManager](https://github.com/northwood-studios/LabAPI/blob/master/LabApi/Loader/Features/Paths/PathManager.cs).
