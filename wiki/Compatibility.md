# Compatibility

| Component | This version |
|---|---|
| Runner (`labtest`) | .NET 10, Windows and Linux x64 |
| Harness and tests | net48, loaded by the server's own Mono runtime |
| SCP:SL validated against | 14.2.7 |
| LabAPI used to build and test | 1.1.7 (minimum declared: 1.1.0) |
| Harmony | pinned to `2.3.6` — LabAPI refuses a plugin built against another version |

The minimum LabAPI version the harness declares isn't a guarantee that
every 1.1.x release works: the harness reaches into internal game details.
Rebuild and rerun your suite after a game or LabAPI update, and treat a
failure on a new version as a real finding — don't work around it by
ignoring an incomplete result.

## Known limits

- **`Dirty.Process` isolation currently degrades to `Dirty.Round`.** A test
  that truly needs a server to itself must be run in its own
  `labtest run --test ...` invocation. See [Writing Tests](Writing-Tests).
- Building the **harness itself** from source only works on Windows (the
  game's assemblies are missing some .NET Framework facades that Linux has
  no fallback for). This only matters if you're building LabTesting from
  source — an ordinary plugin test project builds fine on Linux. See
  [Continuous Integration](Continuous-Integration) for the one case where it
  leaks into a consumer.
- A forced kill (SIGKILL, machine failure) skips cleanup of the working
  copy — use ephemeral CI runners, or the recovery script on a persistent
  machine (see [Troubleshooting](Troubleshooting)).
- A plugin that writes to absolute paths outside the standard LabAPI
  locations, or spawns a detached process, isn't isolated by LabTesting —
  run it in a container or a disposable VM if that matters to you.
