# Chocolatey package: `sideclip`

Installs Sideclip from the official GitHub Release ZIP.

Checksums in `sideclip/tools/chocolateyinstall.ps1` and `sideclip/tools/VERIFICATION.txt` are `REPLACE_ME` until the `v0.1.0` zip exists. Fill them after `scripts/publish-release.ps1` prints the SHA256.

## Pack (local)

```powershell
cd pack/chocolatey/sideclip
choco pack
```

Produces `sideclip.0.1.0.nupkg`.

Do not pack while checksums are still `REPLACE_ME` if you intend to push — Chocolatey install will reject the placeholder.

## Install from local nupkg

Requires the GitHub Release asset `Sideclip-win-x64-v0.1.0.zip` to already exist (the install script downloads it).

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

## First v0.1.0 publish (Guy / VENGEANCE)

1. On Windows, from the repo root:

   ```powershell
   .\scripts\publish-release.ps1
   ```

   This publishes a self-contained `win-x64` folder, zips it to `artifacts\Sideclip-win-x64-v0.1.0.zip`, and prints SHA256.

2. Create the GitHub Release (optionally attach the nupkg after step 4):

   ```powershell
   gh release create v0.1.0 artifacts\Sideclip-win-x64-v0.1.0.zip --title "Sideclip v0.1.0" --notes "First release: tray clipboard, picker, screenshot-path typer."
   ```

3. Replace every `REPLACE_ME` in:

   - `pack/chocolatey/sideclip/tools/chocolateyinstall.ps1`
   - `pack/chocolatey/sideclip/tools/VERIFICATION.txt`

   with the SHA256 printed by the publish script (uppercase hex).

4. Pack:

   ```powershell
   cd pack\chocolatey\sideclip
   choco pack
   ```

5. Commit the checksum fill (or include it on the release branch), then optionally:

   ```powershell
   gh release upload v0.1.0 sideclip.0.1.0.nupkg
   choco push sideclip.0.1.0.nupkg --source https://push.chocolatey.org/ --api-key <YOUR_KEY>
   ```

## Bumping a version

1. Set `Version` / `AssemblyVersion` in `Sideclip.csproj` to `X.Y.Z`.
2. Publish GitHub release `vX.Y.Z` with `Sideclip-win-x64-vX.Y.Z.zip` (`.\scripts\publish-release.ps1 -Version X.Y.Z`).
3. Update `sideclip.nuspec` version.
4. Update `$version` and SHA256 in `tools/chocolateyinstall.ps1`.
5. Update `tools/VERIFICATION.txt`.
6. `choco pack` and push.
