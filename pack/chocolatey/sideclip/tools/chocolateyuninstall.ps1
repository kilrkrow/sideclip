$ErrorActionPreference = 'Stop'

Uninstall-BinFile -Name 'sideclip'

$shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Sideclip.lnk'
if (Test-Path $shortcut) {
  Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
}

$skillDest = Join-Path $env:USERPROFILE '.agents\skills\sideclip'
if (Test-Path -LiteralPath $skillDest) {
  Remove-Item -LiteralPath $skillDest -Recurse -Force -ErrorAction SilentlyContinue
}
