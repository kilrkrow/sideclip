# Chocolatey package: `sideclip`

Installs Sideclip from the official GitHub Release ZIP (framework-dependent; depends on Chocolatey package `dotnet-8.0-desktopruntime` for the .NET 8 Desktop Runtime x64), shims `sideclip` onto PATH, and copies the skill to `%USERPROFILE%\.agents\skills\sideclip` (Grok, Codex) and `%USERPROFILE%\.claude\skills\sideclip` (Claude Code).

Current package metadata version is **0.3.0** (`sideclip/sideclip.nuspec`). After `.\scripts\publish-release.ps1 -Version 0.3.0`, fill the ZIP SHA256 into `sideclip/tools/chocolateyinstall.ps1` and `sideclip/tools/VERIFICATION.txt` to match `artifacts\Sideclip-win-x64-v0.3.0.zip`. Do not invent checksums; use the script output.

Release asset pattern:

- URL: `https://github.com/kilrkrow/sideclip/releases/download/v0.3.0/Sideclip-win-x64-v0.3.0.zip`
- SHA256: take from `publish-release.ps1` / `Get-FileHash` of that ZIP

## Pack (local)

Syncs `.agents/skills/sideclip` into `tools/skill/sideclip`, then packs:

```powershell
.\scripts\pack-chocolatey.ps1
```

Or:

```powershell
.\scripts\sync-agent-skill.ps1
cd pack/chocolatey/sideclip
choco pack
```

Produces `sideclip.0.3.0.nupkg`.

## Install from local nupkg

Requires the GitHub Release asset `Sideclip-win-x64-v0.3.0.zip` and a matching SHA256 in `tools/chocolateyinstall.ps1`.

```powershell
choco install sideclip -y --source "'.;https://community.chocolatey.org/api/v2/'"
# or
choco install sideclip -y -s .
```

## Push to community feed (maintainers)

```powershell
choco push sideclip.0.3.0.nupkg --source https://push.chocolatey.org/ --api-key <YOUR_KEY>
```

Requires a [Chocolatey.org](https://community.chocolatey.org) account and package moderation for first publish.

Optionally attach the nupkg to the GitHub Release:

```powershell
gh release upload v0.3.0 sideclip.0.3.0.nupkg
```

## Bumping a version

1. Set `Version` / `AssemblyVersion` in `Sideclip.csproj` to `X.Y.Z` (keep packaging docs aligned; do not invent a new version policy here).
2. `.\scripts\sync-agent-skill.ps1`
3. Publish GitHub release `vX.Y.Z` with `Sideclip-win-x64-vX.Y.Z.zip` (`.\scripts\publish-release.ps1 -Version X.Y.Z`).
4. Update `sideclip.nuspec` version.
5. Update `$version` and SHA256 in `tools/chocolateyinstall.ps1`.
6. Update `tools/VERIFICATION.txt`.
7. `.\scripts\pack-chocolatey.ps1` and push.
