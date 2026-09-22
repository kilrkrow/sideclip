# Sideclip

Windows tray clipboard that sits beside Win+V. It never steals Win+V.

- Watches the clipboard. Non-secrets persist locally. Secrets stay in memory and expire (always-secret for 1Password.exe / 1Password-BrowserSupport owner or foreground; browsers use the heuristic).
- Picker: `Ctrl+Shift+Win+V` (type to filter, Enter paste, Shift+Enter copy).
- Screenshot path: `Ctrl+Shift+Win+P` types the newest file in Pictures\Screenshots.
- Release is tray-only. Hotkeys are assignable from the tray.

## Build

Self-contained `win-x64` folder (release / Chocolatey zip):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\Sideclip.exe`

On Windows, `.\scripts\publish-release.ps1` publishes to `artifacts\publish\win-x64`, zips it to `artifacts\Sideclip-win-x64-v0.1.0.zip`, and prints SHA256.

Defaults never bind `Win+V`, `Ctrl+Win+V`, or `Ctrl+Win+P`.

## Version

`0.1.0` in `Sideclip.csproj` (`Version` / `AssemblyVersion`).

## Chocolatey

See [pack/chocolatey/README.md](pack/chocolatey/README.md). Package id `sideclip` downloads `Sideclip-win-x64-v0.1.0.zip` from GitHub Releases.

## License

[MIT](LICENSE)
