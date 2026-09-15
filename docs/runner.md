# Runner: configuration, isolation and results

## Interface

```text
labtest run|list --config labtesting.json
  --server DIR              source installation
  --server-executable FILE  explicit native binary (relative to server, or absolute)
  --port N                  UDP/TCP, 1..65535
  --timeout N               seconds, 1..86400
  --reports DIR             parent of the persistent reports
  --work DIR                parent of the temporary copies
  --assembly NAME           exact filter, repeatable
  --collection NAME         exact filter, repeatable
  --test ID                 exact filter, repeatable
  --framework-tests         adds the framework's own tests
  --fail-on-skipped
  --keep                    keeps the copy after the run
labtest --selftest
```

`--plugin LabTesting.dll` is kept for framework suites launched without a JSON.
`--force` was removed: no existing file may be overwritten.
Exit codes: **0** success; **1** assertion, test exception or refused skip;
**2** runner, harness, protocol, timeout, crash or incomplete-verdict error.
The server's own exit code is never enough to conclude success.

## JSON configuration

See [the complete example](../examples/labtesting.json).
Paths inside the JSON are relative to the **JSON file**; the `--server`,
`--reports` and `--work` CLI paths are relative to the shell.
`serverExecutable` is relative to the server directory.
Unknown properties are rejected, so typos surface.
The lists hold explicit files, with no implicit globbing.

| Field | Use |
|---|---|
| server, serverExecutable | Installation and native binary |
| harness | LabTesting.dll |
| tests | Test libraries |
| plugins | Plugin under test and required plugins |
| dependencies | Shared libraries |
| files | Relative source, root and target |
| serverSettings | Text pairs for config_gameplay.txt |
| assemblies | Simple names of the selected assemblies |
| collections | Exact collection names |
| testNames | Full identifiers as printed by list |
| frameworkTests | false by default |
| failOnSkipped | false by default; true recommended in CI |
| port, timeoutSeconds, tickrate | 7777, 600, 60 by default |
| reports, work, keep | Storage and retention |

Filters combine as an intersection; several values of one filter form a union.
A filter matching nothing fails. Every selected external assembly must yield at
least one test. To select a single assembly out of a set, also fill in
`assemblies`.

Identifiers look like `Assembly:Namespace.Fixture.Method`. Theory rows carry an
`(index)` suffix. Collisions on an identifier, on a destination (including
case-only differences) and on an assembly's simple name are rejected. Absolute
paths, `..` traversal and DLLs hidden inside `files` are forbidden. The
infrastructure files, the sentinel and `config_sharing.txt` are reserved.

## Isolating each run

The runner creates a unique `labtest-<date>-<uuid>` child of the work directory.
It copies the server's native resources and its runtime directories, without
carrying over AppData or the local policy. It refuses symbolic links inside the
source installation. It launches the native binary from **that copied
installation**, so `ConfigTemplates/` is available there.

`hoster_policy.txt` enables `gamedir_for_configs: true`. LabAPI then uses:

```text
<copy>/AppData/SCP Secret Laboratory/LabAPI/
  plugins/<port>/       harness and declared plugins
  dependencies/<port>/  declared dependencies
  configs/<port>/       declared configurations
```

The server gets a separate `-configpath`, created before launch, holding
`online_mode: false` and `LABTESTING_ENABLED`. The `-stdout` then `-port` order is
preserved. The usual profile variables are redirected to the copy as well. This
isolates the standard paths; it is **not a security sandbox** against a plugin
that writes to absolute paths or reaches the network.

An exclusive per-port lock coordinates LabTesting runners; a UDP and TCP bind
detects a port already in use. A third-party program can still take the port
between that check and the game's own bind: the missing or incomplete verdict then
fails the run. Each worker must use its own port.

A normal shutdown, a timeout, Ctrl+C and SIGTERM all stop the descendants. Windows
uses a Job Object with kill-on-close; Linux uses a process group created by
`setsid`, on top of stopping the tree. `-id<PID>` also ties the server to the
runner. A child that deliberately detaches can escape the Linux group: use an
ephemeral machine or container for plugins that spawn daemons.

No program can run its `finally` after SIGKILL, a machine failure or a forced
system shutdown. The workflow has an `always()` fallback cleanup; a GitHub-hosted
runner is ephemeral. On a persistent Linux agent,
`python3 scripts/cleanup.py /path/.labtest-work` recovers copies marked abandoned;
**do not run that recovery against active suites**. On Windows the Job Object
kills the tree when the runner is forcibly closed, but the disk copy must be
removed after checking its marker. Cleaning copies never deletes reports.

## Discovery and protocol

`list` starts the server, loads the plugins, discovers the tests and exits without
starting the round or invoking the fixtures. It therefore needs the game
installed, just like `run`.

The harness writes and flushes immediately:

1. A `kind: plan` object holding every test, collection, assembly and version.
2. One result per test: outcome, durations in ticks and milliseconds, isolation,
   assertions, swallowed exceptions and any harness error.
3. A single final `kind: summary`.

The runner uses a strict JSON parser and recomputes the counters from the results.
It requires plan and results to match, no duplicates, a consistent final summary
and a zero server exit code. In list mode the plan must be non-empty and the
summary must count zero results.

## Reports

Every run keeps `stdout.log`, `stderr.log`, `deployment.json`,
`labtesting-results.jsonl`, `junit.xml` and `summary.md` in its own directory.
A failure before launch may produce only JUnit and Markdown; a crash before the
harness arms produces no JSONL. The deployment manifest holds the identities and
SHA-256 of the copied files.

JUnit keeps the JSON details of every result and creates explicit errors for
missing cases and infrastructure failures. It stays usable after a crash or a
truncated JSON line. The Markdown holds the results, durations, collections,
failure details and versions.
