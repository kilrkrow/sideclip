$ErrorActionPreference = 'Stop'

Uninstall-BinFile -Name 'sideclip'

$shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Sideclip.lnk'
if (Test-Path $shortcut) {
  Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
}

foreach ($rel in @('.agents\skills\sideclip', '.claude\skills\sideclip')) {
  $skillDest = Join-Path $env:USERPROFILE $rel
  if (Test-Path -LiteralPath $skillDest) {
    Remove-Item -LiteralPath $skillDest -Recurse -Force -ErrorAction SilentlyContinue
  }
}
