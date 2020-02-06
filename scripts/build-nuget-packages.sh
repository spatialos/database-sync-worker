#!/usr/bin/env bash
set -e -u -x -o pipefail

cd "$(dirname "$0")/../"

dotnet run --project BootstrapEnv get-nuget-packages $@ --prefix CSHARP
