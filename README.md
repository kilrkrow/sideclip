# Sideclip

Windows tray clipboard that sits beside Win+V. It never steals Win+V.

- Watches the clipboard. Non-secrets persist locally. Secrets stay in memory and expire (always-secret for 1Password.exe / 1Password-BrowserSupport owner or foreground; browsers use the heuristic).
- Picker: `Ctrl+Shift+Win+V` (type to filter, Enter paste, Shift+Enter copy).
- Screenshot path: `Ctrl+Shift+Win+P` types the newest file in Pictures\Screenshots.
- Release is tray-only. Hotkeys are assignable from the tray.

## Build

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\Sideclip.exe`

Defaults never bind `Win+V`, `Ctrl+Win+V`, or `Ctrl+Win+P`.
