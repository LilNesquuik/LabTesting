param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$Managed = $env:SL_REFERENCES,
    [string]$Output = (Join-Path $PSScriptRoot '../dist')
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath($Output)
if (!$Managed -or !(Test-Path -LiteralPath (Join-Path $Managed 'Assembly-CSharp.dll'))) {
    throw 'Pass -Managed pointing to SCPSL_Data/Managed.'
}
function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)" }
}
Invoke-Dotnet @('build', "$repository/src/LabTesting/LabTesting.csproj", '-c', 'Release',
    "-p:Version=$Version", "-p:SL_REFERENCES=$Managed", "-p:EXILED_REFERENCES=$Managed")
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$harness = "$repository/src/LabTesting/bin/Release/net48"
foreach ($rid in @('linux-x64', 'win-x64')) {
    $stage = Join-Path $outputRoot "LabTesting-$Version-$rid"
    if (Test-Path -LiteralPath $stage) { throw "Output already exists: $stage" }
    New-Item -ItemType Directory -Path "$stage/harness" -Force | Out-Null
    Invoke-Dotnet @('publish', "$repository/src/LabTesting.Runner/LabTesting.Runner.csproj",
        '-c', 'Release', '-r', $rid, '--self-contained', 'true', "-p:Version=$Version",
        '-o', "$stage/runner")
    # Explicit allowlist: no game, Unity, LabAPI or publicized assemblies in distributions.
    Copy-Item -LiteralPath "$harness/LabTesting.dll","$harness/0Harmony.dll" -Destination "$stage/harness"
    Copy-Item -LiteralPath "$repository/THIRD-PARTY-NOTICES.md" -Destination $stage
    New-Item -ItemType Directory -Path "$stage/tools" | Out-Null
    Copy-Item -LiteralPath "$repository/scripts/install-server.sh","$repository/scripts/verify-release.ps1","$repository/scripts/cleanup.py" -Destination "$stage/tools"
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $manifest = [ordered]@{
        version = $Version
        runtime = $rid
        runnerTarget = 'net10.0'
        harnessTarget = 'net48'
        labapi = '1.1.7'
        harmony = '2.3.6'
        serverAssemblySha256 = (Get-FileHash -LiteralPath "$Managed/Assembly-CSharp.dll" -Algorithm SHA256).Hash
        files = $files
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$stage/manifest.json" -Encoding utf8
    Compress-Archive -Path "$stage/*" -DestinationPath "$stage.zip"
}
Get-ChildItem -LiteralPath $outputRoot -Filter "LabTesting-$Version-*.zip" | Sort-Object Name | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
} | Set-Content -LiteralPath "$outputRoot/SHA256SUMS" -Encoding ascii
