# Sideclip

Windows tray clipboard that sits beside Win+V. It never steals Win+V.

- Watches the clipboard. Non-secrets persist locally. Secrets stay in memory and expire (always-secret for 1Password.exe / 1Password-BrowserSupport owner or foreground; browsers use the heuristic).
- Picker: `Ctrl+Shift+Win+V` (type to filter, Enter paste, Shift+Enter copy).
- Screenshot path: `Ctrl+Shift+Win+P` types the newest file in Pictures\Screenshots.
- Release is tray-only. Hotkeys and history limits are set in **Settings...** from the tray.

## History limits

Settings has separate limits for stored **text** clips and **image** clips, each a number or **Unlimited**.

- New profiles start at 100 text / 25 image clips. Profiles that already have history (or a settings file from before limits existed) start at **Unlimited**; nothing is removed until you choose a limit.
- At the limit, the oldest clip of that type is removed to make room, and its image file is deleted. Pinned clips are never removed and do not count toward a limit (there is no pin UI yet).
- Lowering a limit below what is stored asks first, with the exact number of clips that will be removed (oldest first). Cancel removes nothing. Raising a limit or choosing Unlimited never removes anything.
- Secrets are not stored on disk and are not counted; they still expire by Secret TTL.
- Stored in `settings.json` as `MaxTextClips` / `MaxImageClips` (`0` = Unlimited).

## Data folder

Settings, history and images live in `%LocalAppData%\Sideclip`. Set the environment variable `SIDECLIP_DATA_DIR` to use another folder (for example a throwaway test profile). When it is set, Sideclip also leaves the "Start with Windows" registry value alone.

## Tests

```powershell
dotnet test Sideclip.Tests -c Release
```

Tests run against a temporary data folder (set `SIDECLIP_TEST_TEMP` to choose where) and never touch your real profile.

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
