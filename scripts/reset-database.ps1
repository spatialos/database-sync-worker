$ErrorActionPreference = "Stop"

cd "$PSScriptRoot/../"

& dotnet run -p ./Bootstrap/Bootstrap.csproj -- run-database-commands -c "DROP DATABASE IF EXISTS items" "CREATE DATABASE items;" --no-database $Args
& dotnet run -p ./Bootstrap/Bootstrap.csproj -- run-database-commands -c "CREATE TABLE metrics (time TIMESTAMP, name VARCHAR, value INT);"  --postgres-database "items" $Args
& dotnet run -p ./Bootstrap/Bootstrap.csproj -- create-database-table --table-name "items" --postgres-database "items" $Args
