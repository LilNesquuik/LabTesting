param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Package))
try {
    $expected = @('lib/net48/LabTesting.dll', 'tools/harness/LabTesting.dll', 'tools/harness/0Harmony.dll',
        'tools/runner/labtest.dll', 'tools/runner/labtest.deps.json', 'tools/runner/labtest.runtimeconfig.json',
        'build/LabTesting.props', 'build/LabTesting.targets', 'tools/cleanup.py', 'README.md', 'THIRD-PARTY-NOTICES.md', 'manifest.json')
    $names = @($archive.Entries | ForEach-Object FullName)
    if (@($names | Group-Object | Where-Object Count -GT 1).Count) { throw 'Duplicate ZIP entry.' }
    foreach ($name in $expected) { if ($name -cnotin $names) { throw "Missing payload: $name" } }
    foreach ($name in $names) {
        if ($name -notin $expected -and $name -notmatch '^(LabTesting\.nuspec|\[Content_Types\]\.xml|_rels/\.rels|package/services/metadata/core-properties/[^/]+\.psmdcp)$') {
            throw "Unexpected package content: $name"
        }
    }
    function Read-Entry([string]$Name) {
        $reader = [IO.StreamReader]::new($archive.GetEntry($Name).Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    [xml]$nuspec = Read-Entry 'LabTesting.nuspec'
    if ($nuspec.package.metadata.id -cne 'LabTesting' -or $nuspec.package.metadata.version -cne $Version) {
        throw 'Unexpected NuGet identity or version.'
    }
    $manifest = Read-Entry 'manifest.json' | ConvertFrom-Json
    if ($manifest.version -cne $Version -or $manifest.files.Count -ne 11) { throw 'Invalid manifest.' }
    if (@($manifest.files | Group-Object path | Where-Object Count -GT 1).Count) { throw 'Duplicate manifest entry.' }
    foreach ($entry in $manifest.files) {
        if ($entry.path -cnotin $expected) { throw "Invalid manifest entry: $($entry.path)" }
        $stream = $archive.GetEntry($entry.path).Open()
        try {
            $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
            if ($actual -cne $entry.sha256) { throw "SHA-256 mismatch: $($entry.path)" }
        } finally { $stream.Dispose() }
    }
    $runtime = Read-Entry 'tools/runner/labtest.runtimeconfig.json' | ConvertFrom-Json
    if ($runtime.runtimeOptions.tfm -cne 'net10.0' -or !$runtime.runtimeOptions.framework) {
        throw 'Expected a portable framework-dependent .NET 10 runner.'
    }
    Write-Output "Verified LabTesting ${Version}: exact payload, portable runner and all SHA-256 hashes."
} finally { $archive.Dispose() }
