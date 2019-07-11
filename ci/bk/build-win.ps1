$ErrorActionPreference = "Stop"

cd "$PSScriptRoot/../../"

./scripts/build-nuget-packages.ps1

& dotnet build ./Workers.sln -p:Platform=x64

./scripts/publish-windows-workers.ps1
./scripts/publish-osx-workers.ps1
./scripts/publish-linux-workers.ps1
