# GitHub Actions

## NuGet template

[nuget-tests.yml](../templates/github/nuget-tests.yml) is the simplest template:
LabTesting's version lives only in the test project's `PackageReference`, with no
archive to download and no SHA-256 to keep up to date. Copy the file to
`.github/workflows/tests.yml` and fill in `TEST_PROJECT`. The job installs the
native prerequisites and the 996560 server, then calls `dotnet build -t:LabTesting`
with the `LabTestingServer`, `LabTestingReports`, `LabTestingWork` and
`LabTestingTimeout` properties.

## Public template (standalone archive)

Copy [labtesting.yml](../templates/github/labtesting.yml) and
[labtesting-reusable.yml](../.github/workflows/labtesting-reusable.yml) into the
consuming repository's `.github/workflows/`. The first triggers the suite on push,
pull_request and workflow_dispatch. The second exposes `workflow_call`: checkout,
.NET 10, native prerequisites, SteamCMD 996560, the LabTesting distribution, the
build, a bounded run and the published results.

Replace:

| Input | Value |
|---|---|
| release-repository | owner/repository publishing LabTesting |
| release-version | exact version without v, for example 0.2.0 |
| archive-sha256 | SHA-256 of that version's Linux archive |
| config | path to the JSON, defaults to examples/labtesting.json |
| artifact-name | unique prefix per parallel call, defaults to labtesting |
| build-command | command building the plugin and its tests |
| dependency-build-command | optional command for public or private dependencies |

The template needs no secret for a public release with public dependencies. The
release must be published, not a draft. Checking the archive hash avoids trusting
a later change to the tag.

`SL_REFERENCES` and `EXILED_REFERENCES` point at the freshly installed assemblies.
The build receives the distribution's harness through `LabTestingPath`. Adapt the
plugin's own references in its build command. Pin NuGet versions and dependency
refs; SteamCMD installs the current public build of the game, whose real version
appears in the report.

For several parallel calls in one workflow, give each a distinct artifact-name.
Suites sharing a machine must also use distinct ports in their configurations.

The runner has a 600-second timeout; the step and the job allow more, so the
runner has time to finish and write its reports. Logs and JUnit are uploaded with
`if: always()`, the Markdown is appended to the GitHub summary, and a final
cleanup recovers the marked copies. An abrupt VM shutdown cannot guarantee the
files are uploaded.

The reusable workflow can also be called from a central repository:
`YOUR-ORG/LabTesting/.github/workflows/labtesting-reusable.yml@COMMIT_SHA`.
Pin that commit and allow reusable workflows in the Actions settings.

## Private dependencies

Use [private-dependencies.yml](../templates/github/private-dependencies.yml).
Fill in `dependency-repository`, `dependency-ref` (full SHA) and
`dependency-build-command`. Then declare the resulting DLLs in the JSON, for
example `../.dependencies/source/src/RequiredPlugin/bin/Release/net48/RequiredPlugin.dll`
under `plugins`. A library without a Plugin class goes under `dependencies`.

Required secret: **DEPENDENCY_TOKEN**, a token with read access to that private
repository. The checkout does not persist credentials. The secret is passed
explicitly to the called workflow and only to the step fetching the dependency.
The build command does not need it.

**Fork pull requests**: GitHub does not pass ordinary secrets. The private suite
fails explicitly with an access-unavailable message; it is not declared successful
and not replaced by the framework tests. Do not use `pull_request_target` to run
fork code with the secrets. After reviewing the code, a maintainer can take the
changes onto a trusted branch of the main repository. To keep coverage on forks,
add an independent public suite.

## Required check before merging

Run a first pull request so the check appears, then in the branch rules or ruleset
enable "Require status checks to pass" and select the suite's check
(`integration / suite`, name to confirm on that first pull request). Require it on
protected branches; do not add `continue-on-error`. With private dependencies, a
fork pull request stays blocked until the trusted run succeeds.

## The framework's own CI

`runner.yml` builds and runs the game-free checks on Ubuntu and Windows.
`server-validation.yml` first builds the net48 assemblies on `windows-latest`,
publishes them as an artifact, then runs the same sample plugin on Ubuntu and
Windows against a real SteamCMD install. It is manual and reusable.

net48 is built on Windows only because the game's `Managed` directory does not
carry the framework's `System.*` facades and a Linux runner has no targeting pack
to supply them. The DLLs built on Windows are the ones executed on Linux, which
matches production: SCP:SL servers run on Linux. The constraint is specific to the
harness; a plugin's test project builds normally on Linux.

`release.yml` prepares the versioned archives and creates a **draft** on a tag;
publish the draft after reviewing the results. `nuget-release.yml` builds the
package, validates it on Windows and Ubuntu against a real server, then publishes
it to NuGet.org and GitHub Packages once the release is published. NuGet.org uses
OIDC trusted publishing: no stored API key, only the **NUGET_USER** secret and a
policy declared on nuget.org. Its `workflow_dispatch` with `publish: false` runs
the validation alone.

Sources: [reusable workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/reusing-workflow-configurations),
[artifacts](https://docs.github.com/en/actions/tutorials/store-and-share-data),
[secrets and forks](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository).
