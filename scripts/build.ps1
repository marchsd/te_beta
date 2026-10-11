# Builds the solution and runs tests. Requires the .NET 8 SDK (https://dotnet.microsoft.com/download).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet build TerrariaDesktopWorld.sln -c Release
dotnet run --project tests/DesktopWorld.Tests -c Release
