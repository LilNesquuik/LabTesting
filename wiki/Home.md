# LabTesting

Run tests against a **real SCP:SL dedicated server** instead of mocking the
game. A harness loads inside the server next to your plugin (net48); a
standalone CLI, `labtest` (.NET 10, Windows and Linux), drives the whole run
and reports pass/fail.

## Pages

| Page | For |
|---|---|
| [Getting Started](Getting-Started) | Install, scaffold a project, run your first suite |
| [Writing Tests](Writing-Tests) | Attributes, assertions, players, events, commands, cleanup |
| [Configuration Reference](Configuration-Reference) | `labtesting.json` fields, CLI flags, MSBuild properties |
| [Continuous Integration](Continuous-Integration) | GitHub Actions templates, required checks, private dependencies |
| [Troubleshooting](Troubleshooting) | Symptom → what to check |
| [Compatibility](Compatibility) | Supported versions and platform notes |

## Is this for you?

Use LabTesting to catch regressions that only show up inside a running game:
an event that stops firing, a command that throws, state that leaks into the
next round. It complements, not replaces, ordinary unit tests — logic that
doesn't touch the game belongs in plain xUnit/NUnit, which run in seconds
instead of spinning up a server.

## The two moving parts

- **The harness** (`LabTesting.dll`, net48) — loads as a dependency next to
  your plugin, discovers your `[Fact]`/`[Theory]` methods and runs them.
- **The runner** (`labtest`, .NET 10) — copies the dedicated server into a
  throwaway folder, deploys your plugin and tests into it, starts the server,
  collects the results, cleans up.

You never call the harness directly — `labtest run`, or the MSBuild targets
the NuGet package adds, do that for you.
