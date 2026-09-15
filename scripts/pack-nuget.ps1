param(
    [Parameter(Mandatory)][ValidatePattern('^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$Managed = $env:SL_REFERENCES,
    [string]$Output = (Join-Path $PSScriptRoot '../dist/nuget'),
    [string]$RepositoryUrl = ''
)
$ErrorActionPreference = 'Stop'
$Version = $Version -replace '^v', ''
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath($Output)
if (!$Managed -or !(Test-Path -LiteralPath (Join-Path $Managed 'Assembly-CSharp.dll'))) {
    throw 'Pass -Managed pointing to the server SCPSL_Data/Managed directory.'
}
$Managed = [IO.Path]::GetFullPath($Managed)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$package = Join-Path $outputRoot "LabTesting.$Version.nupkg"
if (Test-Path -LiteralPath $package) { throw "Package already exists: $package. Choose a fresh output directory." }
$stage = Join-Path $outputRoot ('.staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path "$stage/harness" | Out-Null
function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)" }
}
Invoke-Dotnet @('build', "$repository/src/LabTesting/LabTesting.csproj", '-c', 'Release',
    "-p:Version=$Version", "-p:SL_REFERENCES=$Managed", "-p:EXILED_REFERENCES=$Managed")
Invoke-Dotnet @('publish', "$repository/src/LabTesting.Runner/LabTesting.Runner.csproj", '-c', 'Release',
    '--self-contained', 'false', '-p:UseAppHost=false', "-p:Version=$Version", '-o', "$stage/runner")
Invoke-Dotnet @("$stage/runner/labtest.dll", '--selftest')
Copy-Item -LiteralPath "$repository/src/LabTesting/bin/Release/net48/LabTesting.dll",
    "$repository/src/LabTesting/bin/Release/net48/LabTesting.pdb",
    "$repository/src/LabTesting/bin/Release/net48/0Harmony.dll" -Destination "$stage/harness"
$files = @(
    @{ path = 'lib/net48/LabTesting.dll'; source = "$stage/harness/LabTesting.dll" }
    @{ path = 'lib/net48/LabTesting.pdb'; source = "$stage/harness/LabTesting.pdb" }
    @{ path = 'tools/harness/LabTesting.dll'; source = "$stage/harness/LabTesting.dll" }
    @{ path = 'tools/harness/0Harmony.dll'; source = "$stage/harness/0Harmony.dll" }
    @{ path = 'tools/runner/labtest.dll'; source = "$stage/runner/labtest.dll" }
    @{ path = 'tools/runner/labtest.deps.json'; source = "$stage/runner/labtest.deps.json" }
    @{ path = 'tools/runner/labtest.runtimeconfig.json'; source = "$stage/runner/labtest.runtimeconfig.json" }
    @{ path = 'build/LabTesting.props'; source = "$repository/packaging/build/LabTesting.props" }
    @{ path = 'build/LabTesting.targets'; source = "$repository/packaging/build/LabTesting.targets" }
    @{ path = 'tools/cleanup.py'; source = "$repository/scripts/cleanup.py" }
    @{ path = 'tools/server/install-server.sh'; source = "$repository/scripts/install-server.sh" }
    @{ path = 'tools/server/install-server.ps1'; source = "$repository/scripts/install-server.ps1" }
    @{ path = 'README.md'; source = "$repository/packaging/README.md" }
    @{ path = 'THIRD-PARTY-NOTICES.md'; source = "$repository/THIRD-PARTY-NOTICES.md" }
) | ForEach-Object {
    [ordered]@{ path = $_.path; sha256 = (Get-FileHash -LiteralPath $_.source -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{
    version = $Version
    harnessTarget = 'net48'
    runnerTarget = 'net10.0'
    runnerDeployment = 'framework-dependent'
    labapi = '1.1.7'
    harmony = '2.3.6'
    serverAssemblySha256 = (Get-FileHash -LiteralPath "$Managed/Assembly-CSharp.dll" -Algorithm SHA256).Hash.ToLowerInvariant()
    files = @($files)
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$stage/manifest.json" -Encoding utf8
Invoke-Dotnet @('pack', "$repository/packaging/LabTesting.Package.csproj", '-c', 'Release',
    "-p:Version=$Version", "-p:PackageVersion=$Version", "-p:PayloadDirectory=$stage",
    "-p:RepositoryUrl=$RepositoryUrl", '-o', $outputRoot)
& "$PSScriptRoot/verify-nuget.ps1" -Package $package -Version $Version
(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' +
    [IO.Path]::GetFileName($package) | Set-Content -LiteralPath "$package.sha256" -Encoding ascii
Write-Output "NuGet ready: $package"
