$ErrorActionPreference = "Stop"

Set-Location "$PSScriptRoot/../"

New-Item -ItemType Directory -Force -Path nupkgs | Out-Null
& dotnet run --project BootstrapEnv build-nuget-packages $args
if (!$?) {
    exit 1
}
