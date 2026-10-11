#!/usr/bin/env bash
set -euo pipefail
WAR3="${WAR3_DIR:?Set WAR3_DIR to your Warcraft III installation directory}"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
COMPAT="${STEAM_COMPAT_DATA_PATH:-$SCRIPT_DIR/wineprefixes/war3-proton-exp}"
PROTON="${PROTON:-$HOME/.local/share/Steam/steamapps/common/Proton - Experimental/proton}"
export STEAM_COMPAT_CLIENT_INSTALL_PATH="${STEAM_COMPAT_CLIENT_INSTALL_PATH:-$HOME/.local/share/Steam}"
export STEAM_COMPAT_DATA_PATH="$COMPAT"
mkdir -p "$COMPAT"
cd "$WAR3"
exec "$PROTON" run "./World Editor.exe" "$@"
