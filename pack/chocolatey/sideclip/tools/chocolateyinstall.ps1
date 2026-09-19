$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$version = '0.1.0'
$packageName = 'sideclip'
$zipName = "Sideclip-win-x64-v$version.zip"
$baseUrl = "https://github.com/kilrkrow/sideclip/releases/download/v$version"
$zipUrl = "$baseUrl/$zipName"
$zipPath = Join-Path $toolsDir $zipName
$appOut = Join-Path $toolsDir 'app'

# SHA256 of Sideclip-win-x64-v0.1.0.zip from the official GitHub Release.
# REPLACE_ME after Guy builds the zip on Windows (VENGEANCE) and hashes it.
# Get-FileHash .\Sideclip-win-x64-v0.1.0.zip -Algorithm SHA256
$checksum = 'REPLACE_ME'

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

Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
