#!/usr/bin/env bash
set -e -u -x -o pipefail

cd "$(dirname "$0")/../"

dotnet run -p ./Bootstrap/Bootstrap.csproj -- create-database-table --table-name items --postgres-database "items"
dotnet run -p ./Bootstrap/Bootstrap.csproj -- create-profile --count 10 --postgres-database "items"
dotnet run -p ./Bootstrap/Bootstrap.csproj -- create-profile --count 1 --name "local" --postgres-database "items"
