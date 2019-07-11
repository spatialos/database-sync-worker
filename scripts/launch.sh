#!/usr/bin/env bash
set -e -u -x -o pipefail

cd "$(dirname "$0")/../"

# This causes locking problems when auto-running workers.
# dotnet build -c Debug -p:Platform=x64 SnapshotGenerator/SnapshotGenerator.csproj
spatial alpha local launch --main_config="$(pwd)/config/spatialos.json" --launch_config=config/deployment.json
