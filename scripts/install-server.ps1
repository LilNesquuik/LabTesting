param(
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][string]$SteamCmd
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Server, $SteamCmd | Out-Null
$archive = Join-Path $SteamCmd 'steamcmd.zip'
Invoke-WebRequest https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $SteamCmd -Force
$exe = Join-Path $SteamCmd 'steamcmd.exe'
$marker = Join-Path $Server 'SCPSL_Data/Managed/Assembly-CSharp.dll'
# A freshly downloaded SteamCMD bootstraps itself on its first run and exits before it can apply
# app_update, reporting "Missing configuration". Let it update alone, then ask for the app.
& $exe +quit | Out-Null
foreach ($attempt in 1..3) {
    & $exe +force_install_dir $Server +login anonymous +app_update 996560 validate +quit
    # SteamCMD exit codes are unreliable across versions: trust the installed assemblies instead.
    if (Test-Path -LiteralPath $marker) { return }
    Write-Warning "SteamCMD attempt $attempt left no managed assemblies in $Server."
}
throw "SteamCMD could not install app 996560 into $Server."
