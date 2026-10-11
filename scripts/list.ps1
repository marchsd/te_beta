Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project src/DesktopWorld.Cli -c Release -- list @args
