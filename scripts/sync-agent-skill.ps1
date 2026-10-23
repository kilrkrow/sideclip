<#
.SYNOPSIS
  Copy the canonical Sideclip skill to plugin and Chocolatey pack folders.

.DESCRIPTION
  Source of truth: .agents/skills/sideclip
  Copies:
    plugin/skills/sideclip
    pack/chocolatey/sideclip/tools/skill/sideclip
  With -InstallUser, also copies to ~/.agents/skills/sideclip (Grok and Codex).

.EXAMPLE
  .\scripts\sync-agent-skill.ps1
  .\scripts\sync-agent-skill.ps1 -InstallUser
#>
[CmdletBinding()]
param(
    [switch]$InstallUser
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$src = Join-Path $repoRoot '.agents\skills\sideclip'
if (-not (Test-Path -LiteralPath (Join-Path $src 'SKILL.md'))) {
    throw "Canonical skill not found at $src"
}

function Copy-SkillTo {
    param([string]$Dest)
    $parent = Split-Path -Parent $Dest
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    if (Test-Path -LiteralPath $Dest) {
        Remove-Item -LiteralPath $Dest -Recurse -Force
    }
    Copy-Item -LiteralPath $src -Destination $Dest -Recurse
    Write-Host "Synced $Dest"
}

Copy-SkillTo (Join-Path $repoRoot 'plugin\skills\sideclip')
Copy-SkillTo (Join-Path $repoRoot 'pack\chocolatey\sideclip\tools\skill\sideclip')

if ($InstallUser) {
    $user = Join-Path $env:USERPROFILE '.agents\skills\sideclip'
    Copy-SkillTo $user
    Write-Host "Grok: /sideclip    Codex: `$sideclip or /skills"
}
