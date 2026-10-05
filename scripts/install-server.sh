#!/usr/bin/env bash
set -euo pipefail
server="$(realpath -m "${1:?server destination required}")"
steam="$(realpath -m "${2:?steamcmd destination required}")"
mkdir -p "$server" "$steam"
# The caller installs lib32gcc-s1, lib32stdc++6 and the Unity native runtime libraries.
curl --fail --location --retry 3 https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz -o "$steam/steamcmd.tar.gz"
tar -xzf "$steam/steamcmd.tar.gz" -C "$steam"
# A freshly downloaded SteamCMD bootstraps itself on its first run and exits before it can apply
# app_update, reporting "Missing configuration". Let it update alone, then ask for the app.
"$steam/steamcmd.sh" +quit >/dev/null || true
# app_update also fails transiently right after an anonymous login, with the same message, so retry.
for attempt in 1 2 3; do
    "$steam/steamcmd.sh" +login anonymous +force_install_dir "$server" +app_update 996560 validate +quit || true
    # SteamCMD exit codes are unreliable across versions: trust the installed assemblies instead.
    if [ -f "$server/SCPSL_Data/Managed/Assembly-CSharp.dll" ]; then exit 0; fi
    echo "SteamCMD attempt $attempt left no managed assemblies in $server." >&2
    sleep 15
done
exit 1
