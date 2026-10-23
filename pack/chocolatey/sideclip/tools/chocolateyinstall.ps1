$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$version = '0.2.0'
$packageName = 'sideclip'
$zipName = "Sideclip-win-x64-v$version.zip"
$baseUrl = "https://github.com/kilrkrow/sideclip/releases/download/v$version"
$zipUrl = "$baseUrl/$zipName"
$zipPath = Join-Path $toolsDir $zipName
$appOut = Join-Path $toolsDir 'app'

# SHA256 of Sideclip-win-x64-v0.2.0.zip from the official GitHub Release.
# Fill after .\scripts\publish-release.ps1 -Version 0.2.0
$checksum = '868E73FAE7705F3406E6D6D7B5B7D5CEDB5B9A7BA120FB7C84E44F9D856E832C'

Get-ChocolateyWebFile -PackageName $packageName -FileFullPath $zipPath -Url $zipUrl `
  -Checksum $checksum -ChecksumType 'sha256'

Get-ChocolateyUnzip -FileFullPath $zipPath -Destination $appOut -PackageName $packageName

$exe = Join-Path $appOut 'Sideclip.exe'
if (-not (Test-Path $exe)) {
  $found = Get-ChildItem -Path $appOut -Filter 'Sideclip.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($found) {
    $exe = $found.FullName
    $appOut = $found.DirectoryName
  }
}
if (-not (Test-Path $exe)) {
  throw "Sideclip.exe not found after unzip: $appOut"
}

Install-BinFile -Name 'sideclip' -Path $exe

$shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Sideclip.lnk'
Install-ChocolateyShortcut -ShortcutFilePath $shortcut -TargetPath $exe -WorkingDirectory $appOut `
  -Description 'Sideclip — Windows tray clipboard'

$skillSrc = Join-Path $toolsDir 'skill\sideclip'
if (-not (Test-Path -LiteralPath (Join-Path $skillSrc 'SKILL.md'))) {
  $skillSrc = Join-Path $appOut 'skills\sideclip'
}
if (Test-Path -LiteralPath (Join-Path $skillSrc 'SKILL.md')) {
  $skillDest = Join-Path $env:USERPROFILE '.agents\skills\sideclip'
  $skillParent = Split-Path -Parent $skillDest
  New-Item -ItemType Directory -Path $skillParent -Force | Out-Null
  if (Test-Path -LiteralPath $skillDest) {
    Remove-Item -LiteralPath $skillDest -Recurse -Force
  }
  Copy-Item -LiteralPath $skillSrc -Destination $skillDest -Recurse
  Write-Host "Installed Sideclip agent skill to $skillDest"
}

Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
