#!/usr/bin/env bash

set -euxo pipefail

cd "$(dirname "$0")/../"

echo "--- Build NuGet packges"
./scripts/build-nuget-packages.sh

echo "--- Build solution"
dotnet build ./Workers.sln -p:Platform=x64

echo "--- Publish Windows workers"
./scripts/publish-windows-workers.sh

echo "--- Publish OSX workers"
./scripts/publish-osx-workers.sh

echo "--- Publish Linux workers"
./scripts/publish-linux-workers.sh
