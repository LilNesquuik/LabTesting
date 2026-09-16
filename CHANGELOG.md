# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-09-16

**Breaking.** The standalone archive distribution is removed: NuGet is now the
only supported way to install LabTesting. `scripts/package.ps1`,
`scripts/verify-release.ps1`, `labtesting-reusable.yml` and
`templates/github/labtesting.yml` are gone; `release.yml` no longer builds or
attaches `.zip` archives, it only validates and drafts the tag's release.
`templates/github/private-dependencies.yml` is rewritten as a NuGet-based
template (it used to call the now-removed reusable workflow).

### Added

- `labtest init` (`-t:LabTestingInit` through MSBuild) scaffolds `labtesting.json`
  and `.github/workflows/tests.yml` at the repository root from the test
  project's own `ProjectReference` entries, whatever the folder layout. Refuses
  to overwrite either file.
- `labtest run`/`list` auto-discover `labtesting.json` when `--config` is
  omitted, walking up from the current directory the same way the MSBuild
  targets already did — a bare `labtest run --collection X` now works from
  anywhere inside the project.
- `[Trait("name", "value")]` tags a test or a whole class; `--trait` and
  `--exclude-trait` filter by tag, `name=value`, same rules as the other
  filters (each value must match something, `--exclude-trait` is silent
  otherwise).
- The runner auto-detects a local Steam install of the dedicated server
  (app 996560) when `--server`/`LabTestingServer` is not given.
- `CommandLineParser` replaces the runner's hand-rolled argument parsing.

## [0.2.0] - 2026-09-15

**Breaking.** Runner stdout lines now carry a `[labtest]` prefix, `LabTestingList`
included: read `labtesting-results.jsonl` or `junit.xml` instead of parsing stdout.
A consumer pinned below Harmony 2.3.6 no longer restores (NU1605); above 2.3.6 NuGet
only warns (NU1608) and the mismatch still fails when LabAPI loads the plugin.

### Added

- The package declares Harmony as an exact NuGet dependency (`Lib.Harmony [2.3.6]`).
  LabAPI refuses a plugin built against another version, so the mismatch now fails
  the restore instead of surfacing as a load failure at run time.
- `tools/server/install-server.sh` and `tools/server/install-server.ps1` ship in the
  NuGet package, so a consumer no longer reimplements the SteamCMD warm-up and retry
  loop in its own workflow.
- Every runner line on stdout carries the `[labtest]` prefix, and the run ends on one
  summary line — `N passed, N failed, N skipped, N errors` — that also counts the
  infrastructure problems, so a run that produced nothing cannot read as a clean
  `0 failed` in a noisy build log.
- `--framework-directory` warns when the configuration also declares `harness`: the
  NuGet targets supply it, and the JSON value was silently discarded.
- `lib/net48/LabTesting.pdb` ships in the package with SourceLink data, so a stack
  trace inside the harness resolves to file and line against the GitHub sources.
- `.gitattributes`, so the shell scripts keep LF endings whatever a contributor's
  Git is configured to do.
- `.editorconfig` and a Dependabot configuration for the GitHub Actions versions.
- `ContinuousIntegrationBuild` in CI, towards reproducible assemblies.

### Changed

- Every user-facing string is now English: exception messages, console output, the
  Markdown summary and the framework's own test names. The machine-readable
  formats are untouched — JSONL `outcome` values and the JUnit report were already
  language-neutral, so report parsing is unaffected.
- All documentation is now English.
- The version lives in a single `Directory.Build.props`, instead of being repeated
  across the harness, the packaging project, the example consumer and the release
  workflow.
- The net48 assemblies are built on `windows-latest` in CI and the resulting DLLs
  are executed on both systems. The harness cannot compile on Linux; see the known
  limits in `docs/validation.md`. Consuming plugins are unaffected.

### Fixed

- SteamCMD installs are no longer flaky. A freshly downloaded SteamCMD updates
  itself on its first run and exits before applying `app_update`, and `app_update`
  itself fails transiently right after an anonymous login. `install-server.ps1` and
  `install-server.sh` now warm it up, retry three times and judge success on the
  installed assemblies rather than on an exit code that varies between versions.
- `scripts/test-nuget-consumer.ps1` no longer leaves the example projects pointing
  at a throwaway NuGet cache it then deletes, which broke IDE resolution of
  `SamplePlugin` and left a ~100 MB directory behind on every run.

## [0.1.0] - 2026-09-15

First published release.

- net48 harness loaded by the server's Mono, and a portable .NET 10 `labtest`
  runner for Windows and Linux x64.
- A single NuGet package carrying the harness, Harmony 2.3.6, the runner and the
  MSBuild targets: `LabTesting`, `LabTestingList`, `LabTestingSelfTest` and
  `LabTestingCleanup`.
- Standalone win-x64 and linux-x64 archives with a payload manifest and
  `SHA256SUMS`.
- Per-run isolation: a private copy of the server, LabAPI paths redirected through
  `gamedir_for_configs`, a per-port lock, and process-tree shutdown through a
  Windows Job Object or a Linux process group.
- JSONL, JUnit and Markdown reports, kept even after a crash or a timeout.
- MIT license, declared in the package metadata.

[0.3.0]: https://github.com/LilNesquuik/LabTesting/releases/tag/v0.3.0
[0.2.0]: https://github.com/LilNesquuik/LabTesting/releases/tag/v0.2.0
[0.1.0]: https://github.com/LilNesquuik/LabTesting/releases/tag/v0.1.0
