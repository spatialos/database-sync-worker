#!/usr/bin/env bash
set -e -u -x -o pipefail

cd "$(dirname "$0")/../"

dotnet run -p Bootstrap/Bootstrap.csproj -- run-database-commands -c "drop DATABASE IF EXISTS items" "create DATABASE items" --no-database
dotnet run -p Bootstrap/Bootstrap.csproj -- run-database-commands -c "create TABLE metrics (time TIMESTAMP, name VARCHAR, value INT);" --postgres-database "items"
bash scripts/init-database.sh
