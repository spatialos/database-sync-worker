#!/usr/bin/env bash

set -euxo pipefail

cd "$(dirname "$0")/../"

./scripts/build-nuget-packages.sh

dotnet build ./Workers.sln -p:Platform=x64
