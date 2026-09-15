# Validation record — 15 September 2026

## Builds and distributions

- net48 harness and sample plugin built on Windows with the .NET 10 SDK.
- net10.0 runner built and published self-contained for win-x64 and linux-x64.
- Final archives produced in `dist/final/` (Git-ignored).
- Both manifests verified: 198 files per platform, SHA-256 matching.
- Game assemblies excluded from the distributions; harness and Harmony copied
  through an allow-list. The server used is SCP:SL 14.2.7, LabAPI 1.1.7,
  Harmony 2.3.6.
- The net48 sources are never rebuilt on Linux: the same Windows-built DLLs run on
  both systems. See the known limits.

## NuGet package

The checks below were run against a package numbered 1.0.0 during development;
the first published version is **0.1.0**, and `nuget-release.yml` revalidates the
same payload on both systems before publishing.

`LabTesting.<version>.nupkg` is built by `scripts/pack-nuget.ps1`: contents checked
file by file by `scripts/verify-nuget.ps1`, SHA-256 written next to it. Payload:
the net48 harness under `lib/net48` and `tools/harness`, `LabTesting.pdb` with
SourceLink data, Harmony 2.3.6, the framework-dependent .NET 10 `labtest.dll`
runner, the MSBuild targets, `cleanup.py`, the `tools/server` SteamCMD install
scripts, `manifest.json` and the third-party notices. No game, Unity or LabAPI
assembly. Harmony 2.3.6 is also declared as an exact NuGet dependency, so a
consumer pinned to another version fails the restore rather than the run.

Real consumer validation on both systems through
`scripts/test-nuget-consumer.ps1`, with a clean NuGet cache on every run and no
path whatsoever to the LabTesting sources:

| Step | Windows | Ubuntu 24.04, WSL2 |
|---|---|---|
| Restore the package from a local directory | pass | pass |
| Build `examples/NuGetPlugin.Tests` as net48 | pass | pass |
| `-t:LabTestingSelfTest` | pass | pass |
| `-t:LabTestingList` | 4 discovered, exit 0 | 4 discovered, exit 0 |
| `-t:LabTesting` against a real 14.2.7 server | **4/4 passed** | **4/4 passed** |

Reports: `TestResults/nuget/20260915-160006-…` (Windows) and
`20260915-160309-…` (Ubuntu). Both report LabAPI 1.1.7 and Harmony 2.3.6,
identical to the archive flow. On Linux the portable .NET 10 SDK and
PowerShell 7.4.6 were installed under `~/.cache/`, without root.

`verify-nuget.ps1` did block an incomplete package during this work:
`tools/cleanup.py`, required by the `LabTestingCleanup` target, was missing from a
package built before that file was added. The check failed before any test ran.

The same validation ran on GitHub runners, see below.

## Real local runs

| Validation | Windows | Ubuntu 24.04.4, WSL2 x64 |
|---|---|---|
| External plugin plus an explicit test assembly | 4/4, exit 0 | 4/4, exit 0 |
| The framework's own tests | 12/12, exit 0 | 12/12, exit 0 |
| Standalone runner, game-free checks | pass | pass |
| Stopping a process and its child (selftest) | pass | pass |
| list mode on the sample plugin | 4 tests, no body invoked, exit 0 | not run separately |
| Real 1-second timeout | refused, partial reports kept | not run separately |
| Collision with a busy UDP port | refused before launch | not run separately |
| Intentional failure suite | 7 matching results | 7 matching results |

The plugin tests cover a synchronous assertion, static state restored through
IAsyncLifetime, an asynchronous invariant, a dummy player and a LabAPI event. No
personal plugin is deployed into the copies.

Local reports kept under `TestResults/`:

| Suite | Run identifier |
|---|---|
| Framework, Windows | 20260915-033237-0894885299e1477da91469d3755fe22f |
| Plugin, Windows | 20260915-035229-7353965d028f4bed9868956c580c150a |
| list, Windows, after the fix | 20260915-034949-4456b37d696a4c479f800be98a68ca54 |
| Plugin, Ubuntu | 20260915-143101-95377ef1e00047729a459f7b75953d82 |
| Framework, Ubuntu | 20260915-144025-9416a24c9b204e19af98abb5d7e48a35 |

The first list attempt exposed an exit that came too early during FastMenu. The
harness now waits for the lobby to load before leaving list mode. The cleanup also
tolerates transient DLL locks after the process dies and keeps its recovery marker
until the end.

The `tests/FailureSuite` suite was built and run on both systems. It covers a
false assertion, an exception from the body, a skip, a swallowed exception, a
setup that throws, a DisposeAsync that throws and a Dispose that throws. Expected
and observed: **0 passed, 4 failed, 1 skipped, 2 errors**. Exit code **2** was
confirmed explicitly on Ubuntu with the final archive (run
20260915-145227-2f117421cf7b4e71913ba4272d91a061). `scripts/check-failure-suite.ps1`
checks every identifier and its outcome; CI also asserts the expected non-zero
exit code.

## Game-free checks

`labtest --selftest` covers the JSON parser, escaping, the counters, the mandatory
plan, missing or duplicate results, the single final summary, truncated JSON, an
empty plan, harnessError, fail-on-skipped, list mode, refused traversal paths and
the JUnit errors of a partial run. It also starts a process with a child to check
they are really stopped. The final archives pass this command on Windows and
Ubuntu.

The eight YAML workflows and templates are parsed by `scripts/check-workflows.py`,
which also checks the inputs of the local `workflow_call` entries. That syntax
check does not replace a real GitHub Actions run.

## GitHub Actions and publication

Repository: `LilNesquuik/LabTesting`, branch `master`.

`Runner checks` — builds and game-free checks on Ubuntu and Windows — passes on
every push since the initial commit.

`Publish NuGet package`, run as `workflow_dispatch` with `publish: false`
(run 34993885730): **fully green**.

| Job | Result | Duration |
|---|---|---|
| package (windows-latest) | pass, payload and SHA-256 verified | 1 min 49 |
| validate (ubuntu-24.04) | pass, 4/4 tests against a real server | 1 min 32 |
| validate (windows-latest) | pass, 4/4 tests against a real server | 2 min 17 |
| publish | skipped, as expected without a release | — |

`Build release distributions` for tag `v0.1.0` (run 34996667511): **fully green**,
including `Real server validation`, which had never run on GitHub before.

| Job | Result |
|---|---|
| validate / build (windows-latest) | pass, net48 assemblies published as an artifact |
| validate / real-server (ubuntu-24.04) | pass, four suites against a real server |
| validate / real-server (windows-latest) | pass, four suites against a real server |
| package (windows-latest) | pass, both archives and SHA256SUMS |
| release | pass, draft created |

The "empty repository getting a result on a GitHub Ubuntu runner" criterion is
therefore confirmed end to end: SteamCMD installs the server, NuGet restores the
package and the sample suite runs inside the game.

Two CI failures were diagnosed and fixed along the way:

- `ERROR! Failed to install app '996560' (Missing configuration)`. A freshly
  downloaded SteamCMD updates itself on its first run and exits before applying
  `app_update`, and `app_update` also fails transiently right after an anonymous
  login. `scripts/install-server.ps1` and `install-server.sh` now warm it up with a
  bare `+quit`, retry three times and judge success on the installed assemblies
  rather than on an exit code that varies between versions.
- `CS1069` on `Queue<>` when building the harness on Ubuntu. See the known limits.

Remaining steps:

1. Declare the trusted publishing policy on nuget.org and the NUGET_USER secret.
2. Publish the reviewed draft release; publishing triggers the package publication
   and exercises the OIDC path, which `workflow_dispatch` does not test.
3. Copy `nuget-tests.yml` into a consuming repository, fill in the test project and
   the package version, then check a pull request and make the check required.
4. Test the private-dependency example against a real dependent repository and a
   fork pull request.

## Known limits

- **The net48 harness only builds on Windows.** `src/LabTesting` references the
  game's assemblies, including 13 `System.*` facades present on neither server: on
  Windows they resolve from the framework's `Facades/`, while a Linux runner has no
  targeting pack to fall back on and fails with `CS1069` on `Queue<>`. Adding
  `Microsoft.NETFramework.ReferenceAssemblies`, dropping the game's `mscorlib`,
  dropping its `netstandard`, and dropping both were all tried and all fail the
  same way. `server-validation.yml` and `release.yml` therefore build net48 on
  `windows-latest` and run the resulting DLLs on both systems.
  **This does not affect consuming plugins**: an ordinary net48 test project builds
  fine on Linux, as `examples/NuGetPlugin.Tests` proves in the `nuget-release.yml`
  matrix.
- The harness is built against the Windows server's `Managed` directory and then
  loaded by the Linux server's. 123 of the 140 shared DLLs differ byte for byte —
  separate Unity builds — with no API difference observed so far. Since the harness
  publicizes `Assembly-CSharp` and binds to internal members, a difference would
  surface at runtime, not at build time: hence running the suite on Ubuntu in CI
  every time. Every production SCP:SL server runs on Linux, so that is the platform
  that matters.
- SIGKILL or a machine failure allows neither a `finally` nor a guaranteed upload;
  use the fallback cleanup and ephemeral CI runners.
- Plugins writing to absolute paths or spawning detached daemons need additional
  system-level isolation.
- Dirty.Process does not yet recreate a server per test; split those tests into
  separate invocations.
- The AccessSubclasses 53 + 12 reference from the original request was not
  replayed: the external tests in this record are the new SamplePlugin's.
