# Chocolatey package: `sideclip`

Installs Sideclip from the official GitHub Release ZIP.

v0.1.0 checksums are filled in `sideclip/tools/chocolateyinstall.ps1` and `sideclip/tools/VERIFICATION.txt` for:

- URL: https://github.com/kilrkrow/sideclip/releases/download/v0.1.0/Sideclip-win-x64-v0.1.0.zip
- SHA256: `7F1E5597FA317C71EA2EFB6EAEB3EA7213548067E38C0464EDA7BDAAD13157A8`

## Pack (local)

```powershell
cd pack/chocolatey/sideclip
choco pack
```

Produces `sideclip.0.1.0.nupkg`.

## Install from local nupkg

Requires the GitHub Release asset `Sideclip-win-x64-v0.1.0.zip` (already published).

```powershell
choco install sideclip -y --source "'.;https://community.chocolatey.org/api/v2/'"
# or
choco install sideclip -y -s .
```

## Push to community feed (maintainers)

```powershell
choco push sideclip.0.1.0.nupkg --source https://push.chocolatey.org/ --api-key <YOUR_KEY>
```

Requires a [Chocolatey.org](https://community.chocolatey.org) account and package moderation for first publish.

Optionally attach the nupkg to the existing GitHub Release:

```powershell
gh release upload v0.1.0 sideclip.0.1.0.nupkg
```

## Bumping a version

1. Set `Version` / `AssemblyVersion` in `Sideclip.csproj` to `X.Y.Z`.
2. Publish GitHub release `vX.Y.Z` with `Sideclip-win-x64-vX.Y.Z.zip` (`.\scripts\publish-release.ps1 -Version X.Y.Z`).
3. Update `sideclip.nuspec` version.
4. Update `$version` and SHA256 in `tools/chocolateyinstall.ps1`.
5. Update `tools/VERIFICATION.txt`.
6. `choco pack` and push.
