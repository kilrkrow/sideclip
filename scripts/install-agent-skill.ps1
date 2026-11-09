<#
.SYNOPSIS
  Install the Sideclip skill for Grok and Codex (user scope).

.DESCRIPTION
  Syncs the canonical skill into plugin/ and Chocolatey pack folders, then
  copies it to ~/.agents/skills/sideclip (Grok, Codex) and ~/.claude/skills/sideclip
  (Claude Code).

.EXAMPLE
  .\scripts\install-agent-skill.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'sync-agent-skill.ps1') -InstallUser
