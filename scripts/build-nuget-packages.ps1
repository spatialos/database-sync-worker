$ErrorActionPreference = "Stop"

Set-Location "$PSScriptRoot/../"

New-Item -ItemType Directory -Force -Path nupkgs | Out-Null
& dotnet run --project scripts/BuildNugetPackages $args
if (!$?) {
    exit 1
}
