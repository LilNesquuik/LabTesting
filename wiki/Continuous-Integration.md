# Continuous Integration

## Quick setup

Copy [`nuget-tests.yml`](https://github.com/LilNesquuik/LabTesting/blob/master/templates/github/nuget-tests.yml)
to `.github/workflows/tests.yml` (or let `labtest init` do it), fill in
`TEST_PROJECT`, and push. The job installs the dedicated server, then runs
`dotnet build -t:LabTesting`. The version comes from your test project's own
`PackageReference` — nothing else to keep in sync.

## Private plugin dependencies

If a plugin you depend on lives in another repository and isn't published
to NuGet, copy
[`private-dependencies.yml`](https://github.com/LilNesquuik/LabTesting/blob/master/templates/github/private-dependencies.yml)
instead. Fill in `TEST_PROJECT`, `DEPENDENCY_REPOSITORY`, `DEPENDENCY_REF`
(a full commit SHA) and adapt the build command for that dependency. Then
declare the built DLL under `plugins` (or `dependencies` if it has no
`Plugin` class) in `labtesting.json`.

Requires a **`DEPENDENCY_TOKEN`** secret with read access to that private
repository.

**Fork pull requests don't get secrets.** The suite fails explicitly with an
access error instead of silently reporting success — that's intentional, not
a bug. Options: review the code and push it to a trusted branch of the main
repo before running CI on it, or keep a separate public suite that fork PRs
can run without the private dependency.

## Requiring the check before merging

Open one pull request so the check appears, then enable "Require status
checks to pass" on the branch and select it (its name looks like
`integration / suite`). Don't add `continue-on-error` — that defeats the
point.

## One thing to watch on Linux runners

If one of your **own private dependencies** was itself built by referencing
the game's assemblies directly (a `HintPath` to the game's `Managed`
folder, rather than a NuGet package), building it on an Ubuntu runner can
fail with `CS7069` or `CS0122` on a Harmony-internal type. If you hit that,
build that specific job on `windows-latest` instead — the templates default
to Ubuntu because it's the common case.
