param([string]$Reports = 'TestResults/failures')
$ErrorActionPreference = 'Stop'
$run = Get-ChildItem -LiteralPath $Reports -Directory | Sort-Object Name | Select-Object -Last 1
if (!$run) { throw 'No failure suite report.' }
$records = @(Get-Content -LiteralPath (Join-Path $run.FullName 'labtesting-results.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
$summary = @($records | Where-Object kind -EQ 'summary')
if ($summary.Count -ne 1 -or $summary[0].passed -ne 0 -or $summary[0].failed -ne 4 -or $summary[0].skipped -ne 1 -or $summary[0].errors -ne 2) {
    throw 'The intentional failure suite produced unexpected counters.'
}
if ($summary[0].harnessError) { throw 'Unexpected suite-level harness error.' }
$expected = @{
    'BodyFailures.Assertion' = 'failed'
    'BodyFailures.Exception' = 'failed'
    'BodyFailures.Skipped' = 'skipped'
    'BodyFailures.Swallowed' = 'failed'
    'SetupFailure.Body' = 'failed'
    'TeardownFailure.Body' = 'error'
    'DisposeFailure.Body' = 'error'
}
foreach ($entry in $expected.GetEnumerator()) {
    $record = @($records | Where-Object { $_.id -eq "FailureSuite:FailureSuite.$($entry.Key)" })
    if ($record.Count -ne 1 -or $record[0].outcome -ne $entry.Value) { throw "Wrong outcome for $($entry.Key)" }
}
Write-Output 'All 7 intentional failures/skips were correctly reported.'
