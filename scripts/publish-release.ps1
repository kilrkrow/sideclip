<#
.SYNOPSIS
  Publish Sideclip as a self-contained win-x64 folder, zip it, and print SHA256.

.DESCRIPTION
  Intended to run on Windows (e.g. VENGEANCE). This Linux/cloud VM cannot produce
  WPF win-x64 binaries.

  Output: artifacts\Sideclip-win-x64-v<Version>.zip

  After this script, create the GitHub Release (if needed) and put the printed
  SHA256 into pack/chocolatey/sideclip/tools/ (see pack/chocolatey/README.md).

.PARAMETER Version
  Semver without a leading v. Defaults to 0.1.0 (must match Sideclip.csproj / nuspec).

.EXAMPLE
  .\scripts\publish-release.ps1
  .\scripts\publish-release.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
  [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
  throw "Version must be semver without a leading v (got '$Version')."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

$project = Join-Path $repoRoot 'Sideclip.csproj'
if (-not (Test-Path $project)) {
  throw "Sideclip.csproj not found at $project"
}

$artifactRoot = Join-Path $repoRoot 'artifacts'
$publishDir = Join-Path $artifactRoot 'publish\win-x64'
$zipName = "Sideclip-win-x64-v$Version.zip"
$zipPath = Join-Path $artifactRoot $zipName

Write-Host "Publishing Sideclip $Version (self-contained win-x64, single-folder)..."
if (Test-Path $publishDir) {
  Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o $publishDir

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $publishDir 'Sideclip.exe'
if (-not (Test-Path $exe)) {
  throw "Sideclip.exe missing after publish: $exe"
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
if (Test-Path $zipPath) {
  Remove-Item $zipPath -Force
}

# Zip the publish folder contents so Sideclip.exe is at the archive root.
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
$sizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 2)

Write-Host ""
Write-Host "ZIP     $zipPath"
Write-Host "SIZE    $sizeMb MB"
Write-Host "SHA256  $hash"
Write-Host ""
Write-Host "Put this SHA256 in:"
Write-Host "  pack/chocolatey/sideclip/tools/chocolateyinstall.ps1"
Write-Host "  pack/chocolatey/sideclip/tools/VERIFICATION.txt"
Write-Host ""
Write-Host "Create the GitHub Release:"
Write-Host "  gh release create v$Version `"$zipPath`" --title `"Sideclip v$Version`" --notes `"First release: tray clipboard, picker, screenshot-path typer.`""
Write-Host ""
Write-Host "Then pack Chocolatey (after checksums are filled):"
Write-Host "  cd pack/chocolatey/sideclip"
Write-Host "  choco pack"
