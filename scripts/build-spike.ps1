# Builds OverlaySpike and copies it into your Terraria's TerrariaModder\mods folder.
# Usage: .\scripts\build-spike.ps1 [-TerrariaDir "D:\Games\Terraria"]
param([string]$TerrariaDir = "C:\Program Files (x86)\Steam\steamapps\common\Terraria")
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "..\terraria-mod\OverlaySpike"
dotnet build "$proj\OverlaySpike.csproj" -c Release "-p:TerrariaDir=$TerrariaDir"
$dest = Join-Path $TerrariaDir "TerrariaModder\mods\overlay-spike"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$proj\bin\OverlaySpike.dll" $dest -Force
Copy-Item "$proj\manifest.json" $dest -Force
Write-Host "Installed to $dest"
