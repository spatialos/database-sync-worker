#!/usr/bin/env bash
set -e -u -x -o pipefail

cd "$(dirname "$0")/../"

ls -lah ./nupkgs
dotnet run --project scripts/BuildNugetPackages $@
