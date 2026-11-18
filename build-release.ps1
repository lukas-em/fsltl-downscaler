# Builds the release ZIP for Flightsim.to.
# Usage (in the project folder):  powershell -ExecutionPolicy Bypass -File .\build-release.ps1

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

[xml]$props = Get-Content (Join-Path $root "src\Directory.Build.props")
$version = $props.Project.PropertyGroup.Version
$name = "FSLTL-Downscaler-v$version"
$releaseDir = Join-Path $root "release"
$out = Join-Path $releaseDir $name
$zip = Join-Path $releaseDir "$name.zip"

Write-Host "Building $name ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

dotnet publish (Join-Path $root "src\FsltlDownscaler.Gui\FsltlDownscaler.Gui.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item (Join-Path $root "release-files\README.txt") $out
Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE.txt")

Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

$exe = Join-Path $out "FSLTL-Downscaler.exe"
$exeHash = (Get-FileHash $exe -Algorithm SHA256).Hash
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash
$hashFile = Join-Path $releaseDir "SHA256-v$version.txt"
@(
    "FSLTL-Downscaler.exe   SHA256 $exeHash"
    "$name.zip   SHA256 $zipHash"
) | Set-Content -Path $hashFile -Encoding ASCII

$exeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  EXE: $exe ($exeMb MB)"
Write-Host "  ZIP: $zip ($zipMb MB)"
Write-Host "  SHA256: $hashFile"
Write-Host ""
Write-Host "Next: upload the ZIP to https://www.virustotal.com before publishing." -ForegroundColor Yellow
