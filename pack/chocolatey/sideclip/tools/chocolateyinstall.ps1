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
# https://github.com/kilrkrow/sideclip/releases/download/v0.1.0/Sideclip-win-x64-v0.1.0.zip
$checksum = '7F1E5597FA317C71EA2EFB6EAEB3EA7213548067E38C0464EDA7BDAAD13157A8'

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
