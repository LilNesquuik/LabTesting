# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.1] - 2026-09-15

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

### Added

- `lib/net48/LabTesting.pdb` ships in the package with SourceLink data, so a stack
  trace inside the harness resolves to file and line against the GitHub sources.
- `.gitattributes`, so the shell scripts keep LF endings whatever a contributor's
  Git is configured to do.
- `.editorconfig` and a Dependabot configuration for the GitHub Actions versions.
- `ContinuousIntegrationBuild` in CI, towards reproducible assemblies.

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

[0.1.1]: https://github.com/LilNesquuik/LabTesting/releases/tag/v0.1.1
[0.1.0]: https://github.com/LilNesquuik/LabTesting/releases/tag/v0.1.0
