param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$Version,
    [string]$Managed = $env:SL_REFERENCES,
    [string]$Server,
    [switch]$RunServer
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$feed = [IO.Path]::GetFullPath($PackageDirectory)
$package = Join-Path $feed "LabTesting.$Version.nupkg"
& "$PSScriptRoot/verify-nuget.ps1" -Package $package -Version $Version
$consumer = Join-Path $repository 'examples/NuGetPlugin.Tests/NuGetPlugin.Tests.csproj'
# A fresh package cache prevents accidentally testing a previous build of the same version.
$cache = Join-Path $repository ('.labtesting/nuget-test-' + [guid]::NewGuid().ToString('N'))
$config = Join-Path $cache 'NuGet.config'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$sources = [xml]'<configuration><packageSources><clear /></packageSources></configuration>'
foreach ($source in @(@('local', $feed), @('nuget.org', 'https://api.nuget.org/v3/index.json'))) {
    $node = $sources.CreateElement('add')
    $node.SetAttribute('key', $source[0])
    $node.SetAttribute('value', $source[1])
    $sources.configuration.packageSources.AppendChild($node) | Out-Null
}
$sources.Save($config)
$mapping = $sources.CreateElement('packageSourceMapping')
foreach ($source in @(@('local', 'LabTesting'), @('nuget.org', '*'))) {
    $mappedSource = $sources.CreateElement('packageSource')
    $mappedSource.SetAttribute('key', $source[0])
    $pattern = $sources.CreateElement('package')
    $pattern.SetAttribute('pattern', $source[1])
    $mappedSource.AppendChild($pattern) | Out-Null
    $mapping.AppendChild($mappedSource) | Out-Null
}
$sources.configuration.AppendChild($mapping) | Out-Null
$sources.Save($config)
function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)" }
}
$properties = @("-p:LabTestingPackageVersion=$Version", "-p:SL_REFERENCES=$Managed")
Invoke-Dotnet (@('restore', $consumer, '--configfile', $config, '--packages', "$cache/packages") + $properties)
Invoke-Dotnet (@('build', $consumer, '--no-restore', '-c', 'Release') + $properties)
Invoke-Dotnet (@('build', $consumer, '--no-restore', '-c', 'Release', '-t:LabTestingSelfTest') + $properties)
if ($RunServer) {
    if (!$Server) { throw 'Pass -Server when using -RunServer.' }
    Invoke-Dotnet (@('build', $consumer, '--no-restore', '-c', 'Release', '-t:LabTestingList', "-p:LabTestingServer=$Server") + $properties)
    Invoke-Dotnet (@('build', $consumer, '--no-restore', '-c', 'Release', '-t:LabTesting', "-p:LabTestingServer=$Server") + $properties)
}
Write-Output "NuGet consumer validated from a fresh cache: $cache"
