<#
.SYNOPSIS
  Sync the agent skill into the nupkg tree and run choco pack.

.EXAMPLE
  .\scripts\pack-chocolatey.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
& (Join-Path $PSScriptRoot 'sync-agent-skill.ps1')

$pkgDir = Join-Path $repoRoot 'pack\chocolatey\sideclip'
if (-not (Get-Command choco -ErrorAction SilentlyContinue)) {
    throw "choco is not on PATH. Install Chocolatey, then pack from $pkgDir"
}

Push-Location $pkgDir
try {
    choco pack
    if ($LASTEXITCODE -ne 0) {
        throw "choco pack failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
