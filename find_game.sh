#!/bin/bash

# Usage: ./find_sts2_game.sh <APP_ID>

APP_ID="2868840"

if [ -z "$APP_ID" ]; then
    echo "Usage: $0 <APP_ID>"
    exit 1
fi

# Common Steam root locations (Linux & macOS)
STEAM_ROOTS=(
    "$HOME/.steam/steam"
    "$HOME/.local/share/Steam"
    "$HOME/Library/Application Support/Steam"
)

find_in_library() {
    local steamapps="$1/steamapps"
    local manifest="$steamapps/appmanifest_${APP_ID}.acf"

    if [ -f "$manifest" ]; then
        local install_dir
        install_dir=$(grep -i '"installdir"' "$manifest" | awk -F'"' '{print $4}')
        echo "$steamapps/common/$install_dir"
        return 0
    fi
    return 1
}

# Check all library folders
check_steam_root() {
    local root="$1"
    local vdf="$root/steamapps/libraryfolders.vdf"

    # Check the root steamapps itself
    find_in_library "$root" && return 0

    # Parse additional library paths from libraryfolders.vdf
    if [ -f "$vdf" ]; then
        grep -i '"path"' "$vdf" | awk -F'"' '{print $4}' | while read -r lib_path; do
            # Handle escaped backslashes (Windows paths in vdf)
            lib_path="${lib_path//\\\\/\/}"
            find_in_library "$lib_path" && exit 0
        done
    fi
}

found=false
for root in "${STEAM_ROOTS[@]}"; do
    if [ -d "$root" ]; then
        result=$(check_steam_root "$root")
        if [ -n "$result" ]; then
            echo "$result"
            found=true
            break
        fi
    fi
done

if [ "$found" = false ]; then
    echo "Game with App ID '$APP_ID' not found in any Steam library."
    exit 1
fi