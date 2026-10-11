# Live window events + ASCII debug map of your REAL desktop. Pass --mock for the fake desktop.
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project src/DesktopWorld.Cli -c Release -- watch --map @args
