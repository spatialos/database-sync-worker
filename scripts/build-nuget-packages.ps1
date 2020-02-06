$ErrorActionPreference = "Stop"

Set-Location "$PSScriptRoot/../"

New-Item -ItemType Directory -Force -Path nupkgs | Out-Null
& dotnet run --project BootstrapEnv get-nuget-packages $@ --prefix CSHARP
if (!$?) {
    exit 1
}
