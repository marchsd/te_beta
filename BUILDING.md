# Building
Prerequisites: .NET 8 SDK. No NuGet packages are used by Milestone 1, and the tests use a built-in runner.
```
dotnet build TerrariaDesktopWorld.sln -c Release
dotnet run --project tests/DesktopWorld.Tests -c Release   # exit code 1 on failure
```
The Windows project uses plain P/Invoke (no WinForms/WPF), so everything compiles on Linux/macOS; only the
real-desktop source runs on Windows. Offline sandbox tip: add `-p:RestoreSources=<empty dir>`.
