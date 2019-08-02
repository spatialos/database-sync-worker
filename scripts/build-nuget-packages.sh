#!/usr/bin/env bash
set -e -u -x -o pipefail

cd "$(dirname "$0")/../"

rm -rf ~/.nuget/packages/improbable.*

mkdir -p ./nupkgs

# For simplicity, some packages depend on Improbable.WorkerSdkInterop. Make sure that's packaged first.
dotnet pack Improbable/WorkerSdkInterop/Improbable.WorkerSdkInterop -p:Platform=x64 --output "$(pwd)/nupkgs"
dotnet pack Improbable -p:Platform=x64 --output "$(pwd)/nupkgs"
