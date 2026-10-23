# Sideclip

Windows tray clipboard that sits beside Win+V. It never steals Win+V.

- Watches the clipboard. Non-secrets persist locally. Secrets stay in memory and expire (always-secret for 1Password.exe / 1Password-BrowserSupport owner or foreground; browsers use the heuristic).
- Picker: `Ctrl+Shift+Win+V` (type to filter, Enter paste, Shift+Enter copy).
- Screenshot path: `Ctrl+Shift+Win+P` types the newest file in Pictures\Screenshots.
- CLI (tray can be running): `sideclip --shots [n]`, `sideclip --newest`, `sideclip --picker`, `sideclip --pick`.
- Agent skill (Grok / Codex): opens the picker (`--pick`) and inserts the chosen clip. Screenshot paths fall back to a dated text list of the last 3 files (no image ingest).
- Release is tray-only. Hotkeys are assignable from the tray.

## Build

Self-contained `win-x64` folder (release / Chocolatey zip):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\Sideclip.exe`

On Windows, `.\scripts\publish-release.ps1` publishes to `artifacts\publish\win-x64`, zips it to `artifacts\Sideclip-win-x64-v0.2.0.zip` (includes `skills/sideclip`), and prints SHA256.

Defaults never bind `Win+V`, `Ctrl+Win+V`, or `Ctrl+Win+P`.

## Agent skill (Grok and Codex)

`/sideclip` opens the Sideclip picker and inserts the chosen clip into the chat. If the tray is not running, you get a dated text list of the last 3 files in `Pictures\Screenshots`. The skill does not ingest screenshot pixels.

Canonical skill: `.agents/skills/sideclip`. `.\scripts\sync-agent-skill.ps1` copies it to `plugin/skills/sideclip` and the Chocolatey nupkg. Do not edit the copies.

### Install

Chocolatey (`choco install sideclip`) installs the tray, shims `sideclip` onto PATH, and copies the skill to `%USERPROFILE%\.agents\skills\sideclip` (Grok and Codex both scan that folder).

From this repo, user-scope only:

```powershell
.\scripts\install-agent-skill.ps1
```

Grok plugin (this repo is the marketplace):

```powershell
grok plugin marketplace add kilrkrow/sideclip
grok plugin install sideclip --trust
```

Local checkout:

```powershell
grok plugin marketplace add D:\_dev\Sideclip
grok plugin install sideclip --trust
```

Direct plugin path: `grok plugin install kilrkrow/sideclip#plugin --trust`

Codex: same `plugin\` tree (`plugin/.codex-plugin/plugin.json`). After the user-scope copy, `$sideclip` or `/skills`.

`--shots` / `--newest` work without the tray. `--picker` / `--pick` need this build of Sideclip running.

After GitHub release `v0.2.0`, submit a remote catalog entry to [xai-org/plugin-marketplace](https://github.com/xai-org/plugin-marketplace) pointing at `https://github.com/kilrkrow/sideclip.git` (pinned SHA), so Grok `/marketplace` can list it.

## Version

`0.2.0` in `Sideclip.csproj` (`Version` / `AssemblyVersion`).

## Chocolatey

See [pack/chocolatey/README.md](pack/chocolatey/README.md). Package id `sideclip` downloads `Sideclip-win-x64-v0.2.0.zip` from GitHub Releases and installs the agent skill.

## License

[MIT](LICENSE)
