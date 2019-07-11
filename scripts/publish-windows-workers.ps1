$ErrorActionPreference = "Stop"

cd "$PSScriptRoot/../"

& dotnet publish Workers/DatabaseSyncWorker/DatabaseSyncWorker.csproj -r win-x64 -c Release -p:Platform=x64 --self-contained
