$ErrorActionPreference = 'Stop'

Uninstall-BinFile -Name 'sideclip'

$shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Sideclip.lnk'
if (Test-Path $shortcut) {
  Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
}
