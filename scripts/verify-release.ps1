param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content -LiteralPath "$root/manifest.json" -Raw | ConvertFrom-Json
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $entry.path))
    if (!$path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or !$seen.Add($path)) {
        throw "Invalid manifest path: $($entry.path)"
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $entry.sha256) {
        throw "SHA-256 mismatch: $($entry.path)"
    }
}
Write-Output "Verified LabTesting $($manifest.version), $($manifest.runtime), $($seen.Count) files."
