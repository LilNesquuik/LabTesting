# Troubleshooting

| Symptom | Check |
|---|---|
| `labtest: command not found` | The CLI isn't installed globally. Run `dotnet tool install --global LabTesting.Tool`, or drive the MSBuild targets instead (`-t:LabTesting`). |
| Missing DLL / `ReflectionTypeLoadException` | Read the loader exceptions in the JSONL or JUnit report. Add the missing DLL to `dependencies`, or the missing plugin to `plugins`. Don't copy a game/Unity/LabAPI assembly into your own deployment — the server already provides those. |
| No test discovered | Run `list` first. Check `tests` in the JSON, the `[Fact]`/`[Theory]` attributes, your filters, and that the project targets net48. |
| `Port in use` / bind refused | Pick another `--port`. Each parallel worker needs its own. |
| Server process hangs, then times out | Read `stdout.log`/`stderr.log` in the report folder. Re-run with `--keep` to inspect the deployed copy before it's deleted. |
| No verdict file produced at all | The harness never armed — look for a load error, a version mismatch, or a plugin the harness itself depends on being disabled. |
| Exit code non-zero but the summary looks fine | Check the server's own exit code, `--fail-on-skipped`, and for missing/duplicate results — a printed summary alone doesn't mean success. |
| Permission denied on Linux | Check the executable bit on the source server's binary; the runner preserves file permissions when copying. |
| Leftover work folder after a crash | A forced kill (SIGKILL, power loss) skips cleanup. On a persistent Linux machine, run `python3 scripts/cleanup.py <work-dir>` — **never against a directory with an active run**. |
| Your plugin still reads a file from a hardcoded absolute path | LabTesting redirects the *standard* LabAPI config/data paths only. A plugin that hardcodes an absolute path outside those needs its own fix, not a LabTesting setting. |
| `CS7069` / `CS0122` building a private dependency, only on Linux CI | See [Continuous Integration](Continuous-Integration) — build that job on Windows instead. |
