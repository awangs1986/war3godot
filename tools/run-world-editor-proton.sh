#!/usr/bin/env bash
set -euo pipefail
WAR3="${WAR3_DIR:-/path/to/Warcraft.III.R}"
COMPAT="${STEAM_COMPAT_DATA_PATH:-/path/to/war3godot/tools/wineprefixes/war3-proton-exp}"
PROTON="${PROTON:-$HOME/.local/share/Steam/steamapps/common/Proton - Experimental/proton}"
export STEAM_COMPAT_CLIENT_INSTALL_PATH="${STEAM_COMPAT_CLIENT_INSTALL_PATH:-$HOME/.local/share/Steam}"
export STEAM_COMPAT_DATA_PATH="$COMPAT"
mkdir -p "$COMPAT"
cd "$WAR3"
exec "$PROTON" run "./World Editor.exe" "$@"
