# Configuration Reference

## labtesting.json

Paths inside the file are relative to **the file itself**. Unknown fields
are rejected, so a typo shows up immediately instead of being silently
ignored.

```json
{
  "server": "",
  "plugins": ["src/MyPlugin/bin/Release/net48/MyPlugin.dll"],
  "dependencies": [],
  "files": [
    { "source": "fixtures/config.yml", "root": "labapiConfig", "target": "MyPlugin/config.yml" },
    { "source": "fixtures/scenario.json", "root": "data", "target": "scenario.json" }
  ],
  "serverSettings": { "max_players": "8" },
  "traits": [],
  "excludeTraits": [],
  "frameworkTests": false,
  "failOnSkipped": true,
  "port": 7799,
  "timeoutSeconds": 300,
  "reports": "TestResults",
  "work": ".labtest-work"
}
```

| Field | Meaning |
|---|---|
| `server`, `serverExecutable` | Server installation and native binary. Leave `server` empty to auto-detect a local Steam install; always set it explicitly in CI. |
| `plugins` | The plugin under test **and every plugin it depends on**. The harness checks each one is enabled. |
| `dependencies` | Libraries with no `Plugin` class (e.g. `0Harmony.dll`). A DLL must appear only once, even if several plugins need it. |
| `tests` | Test assemblies. The runner adds your own test project's DLL automatically when driven through MSBuild. |
| `files` | Extra files to deploy. `root` is one of `serverConfig`, `labapiConfig` (matching the plugin's own config folder name) or `data` (read back from `test-data/` next to the working directory). |
| `serverSettings` | Extra `key: value` lines for the server's gameplay config. |
| `assemblies` | Restrict discovery to these test assembly names. |
| `collections`, `testNames`, `traits`, `excludeTraits` | Same filters as the CLI flags below — combined with them, not overridden. |
| `frameworkTests` | Also run LabTesting's own test suite. Off by default. |
| `failOnSkipped` | A skipped test fails the run. Recommended `true` in CI. |
| `port`, `timeoutSeconds`, `tickrate` | Default `7777`, `600`, `60`. Give every parallel worker its own port. |
| `reports`, `work`, `keep` | Where results and the throwaway server copy live. `keep: true` keeps the copy after the run, for debugging. |

Do **not** redeclare the harness, Harmony or your own test assembly — the
NuGet package's MSBuild targets add them for you.

## CLI

```text
labtest run|list [--config labtesting.json]
  --server DIR                     source server installation
  --port N                         1..65535
  --timeout N                      seconds, 1..86400
  --reports DIR / --work DIR
  --assembly / --collection / --test / --trait NAME=VALUE   [repeatable, exact match]
  --exclude-trait NAME=VALUE       [repeatable, drops any match]
  --framework-tests
  --fail-on-skipped
  --keep

labtest init [--test-project FILE]
labtest --selftest
```

`--config` is optional: omitted, the runner walks up from the current
directory looking for `labtesting.json` — so `labtest run --collection X`
works from any subdirectory. `--help` on `run`/`list` lists every option.

Filters combine as an **intersection** across kinds, and as a **union**
within one kind — `--collection A --collection B --trait area=inventory`
means "(A or B) and area=inventory". A filter that matches nothing fails
the run rather than silently running an empty suite.

```sh
labtest run --collection Inventory --trait area=weapons
labtest run --test "MyPlugin.Tests:MyPlugin.Tests.GiveTests.Gives_ammo"
```

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Every test passed |
| 1 | An assertion failed, a test threw, or a skip was refused (`failOnSkipped`) |
| 2 | Something prevented a real result: runner/harness error, timeout, crash, incomplete report |

A green server process is not enough on its own — always check this exit
code, not just whether the server started.

## MSBuild targets and properties

Added automatically by the `LabTesting` package reference, on the net48
test project:

| Target | Effect |
|---|---|
| `LabTesting` | Build, then run the suite |
| `LabTestingList` | Build, then discover without running |
| `LabTestingInit` | Scaffold `labtesting.json` and a workflow file |
| `LabTestingSelfTest` | Run the runner's own game-free checks |

| Property | Matches |
|---|---|
| `LabTestingConfig` | `--config` |
| `LabTestingServer` | `--server` |
| `LabTestingReports` | `--reports` |
| `LabTestingWork` | `--work` |
| `LabTestingPort` | `--port` |
| `LabTestingTimeout` | `--timeout` |
| `LabTestingFailOnSkipped` | `--fail-on-skipped` (defaults to `true`) |
| `LabTestingRunOnBuild` | Set to `true` to run the suite on every build, not just on demand |

```sh
dotnet build tests/MyPlugin.Tests -c Release -t:LabTesting -p:LabTestingServer=C:/scpsl -p:LabTestingPort=7801
```
