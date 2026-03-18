#!/bin/bash

RUN_FLAG=""
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STEAM_ID="2868840"

while [[ $# -gt 0 ]]; do
    case $1 in
        -Run|--run)
            RUN_FLAG="yes"
            shift
            ;;
        *)
            shift
            ;;
    esac
done

set -e

echo "Building STS2 MCP..."

if [ -z "$STS2GamePath" ]; then
    STS2GamePath="$HOME/.steam/steam/steamapps/common/Slay the Spire 2"
fi

if [ -z "$GameDataDir" ]; then
    GameDataDir="$STS2GamePath/data_sts2_linux_x86_64"
fi

if [ -f "local.props" ]; then
    echo "Using local.props for configuration"
fi

dotnet build -p:Sts2Os=linux -p:STS2GamePath="$STS2GamePath" -p:GameDataDir="$GameDataDir"

echo "Build complete. Mod copied to: $STS2GamePath/mods/sts2mcp/"

if [ -n "$RUN_FLAG" ]; then
    echo "Stopping existing SlayTheSpire2..."
    pkill -x SlayTheSpire2 || true
    sleep 1

    echo "Launching Slay the Spire 2 via Steam with debug..."
    LD_PRELOAD=/lib/libgcc_s.so.1 HARMONY_DEBUG="true" steam -applaunch "$STEAM_ID" --remote-debug tcp://127.0.0.1:6007 &

    echo "Game launched."
fi