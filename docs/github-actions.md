# GitHub Actions

## NuGet template

[nuget-tests.yml](../templates/github/nuget-tests.yml) is the simplest template:
LabTesting's version lives only in the test project's `PackageReference`, with no
archive to download and no SHA-256 to keep up to date. Copy the file to
`.github/workflows/tests.yml` and fill in `TEST_PROJECT`. The job installs the
native prerequisites and the 996560 server, then calls `dotnet build -t:LabTesting`
with the `LabTestingServer`, `LabTestingReports`, `LabTestingWork` and
`LabTestingTimeout` properties.

## Private dependencies

Copy [private-dependencies.yml](../templates/github/private-dependencies.yml)
instead of `nuget-tests.yml`. Fill in `TEST_PROJECT`, `DEPENDENCY_REPOSITORY` and
`DEPENDENCY_REF` (full SHA), and adapt the dependency's own build command. Then
declare the resulting DLLs in the JSON, for example
`../.dependencies/source/src/RequiredPlugin/bin/Release/net48/RequiredPlugin.dll`
under `plugins`. A library without a Plugin class goes under `dependencies`.

Required secret: **DEPENDENCY_TOKEN**, a token with read access to that private
repository. The checkout does not persist credentials. The build command does not
need it.

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

**Exception**: a plugin that depends on its own private library which was itself
built against the game's assemblies inherits the same constraint. That library's
`HintPath` to the game's `mscorlib`/`netstandard`/`System`/`System.Core` exists so
`Dictionary<>`, `Queue<>` and `Stack<>` share one type identity across the
assembly boundary; on Linux, the SDK pulls
`Microsoft.NETFramework.ReferenceAssemblies` to target net48, and that package's
own `mscorlib` wins over the `HintPath`, producing the same identity mismatches
(`CS7069`, or `CS0122` on a type Harmony embeds as `internal`, such as
`NotNullWhenAttribute`). `templates/github/nuget-tests.yml` and
`templates/github/private-dependencies.yml` default to `ubuntu-24.04` and assume
the ordinary case; a consumer in this situation needs to build that job on
`windows-latest` instead, the same way `server-validation.yml` does.

`release.yml` validates against a real server, then creates a **draft** release on
a tag; publish the draft after reviewing the results. `nuget-release.yml` builds
the package, validates it on Windows and Ubuntu against a real server, then
publishes it to NuGet.org and GitHub Packages once the release is published.
NuGet.org uses OIDC trusted publishing: no stored API key, only the
**NUGET_USER** secret and a policy declared on nuget.org. Its `workflow_dispatch`
with `publish: false` runs the validation alone.

Sources: [reusable workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/reusing-workflow-configurations),
[artifacts](https://docs.github.com/en/actions/tutorials/store-and-share-data),
[secrets and forks](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository).
