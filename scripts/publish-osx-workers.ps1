$ErrorActionPreference = "Stop"

Set-Location "$PSScriptRoot/../"

& dotnet publish Workers/DatabaseSyncWorker/DatabaseSyncWorker.csproj -r osx-x64 -c Release -p:Platform=x64 --self-contained
if (!$?) {
    exit 1
}
