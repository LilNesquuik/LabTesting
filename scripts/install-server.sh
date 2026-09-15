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
"$steam/steamcmd.sh" +force_install_dir "$server" +login anonymous +app_update 996560 validate +quit
test -f "$server/SCPSL_Data/Managed/Assembly-CSharp.dll"
