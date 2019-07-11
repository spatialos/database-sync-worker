$ErrorActionPreference = "Stop"

cd "$PSScriptRoot/../"

& dotnet run -p ./Bootstrap/Bootstrap.csproj -- create-database-table --table-name items --postgres-database "items"
& dotnet run -p ./Bootstrap/Bootstrap.csproj -- create-profile --count 10 --postgres-database "items"
& dotnet run -p ./Bootstrap/Bootstrap.csproj -- create-profile --count 1 --name local --postgres-database "items"
