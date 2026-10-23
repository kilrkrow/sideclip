# Sideclip

Windows tray clipboard that sits beside Win+V. It never steals Win+V.

- Watches the clipboard. Non-secrets persist locally. Secrets stay in memory and expire (always-secret for 1Password.exe / 1Password-BrowserSupport owner or foreground; browsers use the heuristic).
- Picker: `Ctrl+Shift+Win+V` (type to filter, Enter paste, Shift+Enter copy).
- Screenshot path: `Ctrl+Shift+Win+P` types the newest file in `Pictures\Screenshots`.
- CLI: `sideclip --shots [n]`, `sideclip --newest` (no tray needed), `sideclip --picker`, `sideclip --pick` (tray must be running).
- Agent skill (Grok, Codex, Claude Code): opens the picker (`--pick`) and inserts the chosen clip. Screenshot paths fall back to a dated text list of the last 3 files (no image ingest).
- Release is tray-only. Hotkeys are assignable from the tray.

## Install

### GitHub Releases

Download `Sideclip-win-x64-v0.2.0.zip` from [Releases](https://github.com/kilrkrow/sideclip/releases/latest), extract it, and run `Sideclip.exe`.

The zip also includes `skills/sideclip` for a manual skill copy.

### Chocolatey

The `sideclip` 0.2.0 package has been submitted to the Chocolatey community feed and is in moderation. Once it is approved:

```powershell
choco install sideclip
```

Chocolatey installs the tray, shims `sideclip` onto PATH, and copies the skill to `%USERPROFILE%\.agents\skills\sideclip` (Grok, Codex) and `%USERPROFILE%\.claude\skills\sideclip` (Claude Code).

### From source

```powershell
git clone https://github.com/kilrkrow/sideclip.git
cd sideclip
dotnet publish -c Release -r win-x64 --self-contained true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\Sideclip.exe`

## Daily use

- **Picker:** `Ctrl+Shift+Win+V` (type to filter, Enter paste, Shift+Enter copy).
- **Screenshot path:** `Ctrl+Shift+Win+P` types the newest file in `Pictures\Screenshots`.
- Tray icon for history, settings, and About.
- Hotkeys are configurable from the tray. Defaults never bind `Win+V`, `Ctrl+Win+V`, or `Ctrl+Win+P`.

## CLI

`sideclip` is on PATH after Chocolatey. From a GitHub zip, call `Sideclip.exe` with the same flags.

```powershell
sideclip --shots [n]     # n newest files in Pictures\Screenshots as JSON (default 3)
sideclip --newest        # newest screenshot path
sideclip --picker        # open the clipboard picker
sideclip --pick          # open the picker; print the chosen clip as JSON
sideclip --help
```

`--shots` and `--newest` work without the tray. `--picker` and `--pick` need this build of Sideclip running.

See [docs/CLI.md](docs/CLI.md).

## Agent skills

`/sideclip` opens the Sideclip picker and inserts the chosen clip into the chat. If the tray is not running, you get a dated text list of the last 3 files in `Pictures\Screenshots`. The skill does not ingest screenshot pixels.

Canonical skill: `.agents/skills/sideclip`. `.\scripts\sync-agent-skill.ps1` copies it to `plugin/skills/sideclip`, `pack/chocolatey/sideclip/tools/skill/sideclip` (packed into the nupkg), and `.claude/skills/sideclip` (project-scope Claude Code). Do not edit the copies.

### Install the skill

From this repo, user-scope only:

```powershell
.\scripts\install-agent-skill.ps1
```

That copies to `%USERPROFILE%\.agents\skills\sideclip` (Grok, Codex) and `%USERPROFILE%\.claude\skills\sideclip` (Claude Code).

Grok plugin (this repo is the marketplace):

```powershell
grok plugin marketplace add kilrkrow/sideclip
grok plugin install sideclip --trust
```

From a local clone (run in the repo root, where `.grok-plugin/marketplace.json` lives):

```powershell
grok plugin marketplace add .
grok plugin install sideclip --trust
```

Direct plugin path: `grok plugin install kilrkrow/sideclip#plugin --trust`

Codex: same `plugin\` tree (`plugin/.codex-plugin/plugin.json`). After the user-scope copy, `$sideclip` or `/skills`.

After GitHub release `v0.2.0`, submit a remote catalog entry to [xai-org/plugin-marketplace](https://github.com/xai-org/plugin-marketplace) pointing at `https://github.com/kilrkrow/sideclip.git` (pinned SHA), so Grok `/marketplace` can list it.

## Build and release zip

On Windows, `.\scripts\publish-release.ps1` publishes to `artifacts\publish\win-x64`, zips `artifacts\Sideclip-win-x64-v0.2.0.zip` (includes `skills/sideclip`), and prints SHA256.

## Version

`0.2.0` in `Sideclip.csproj` (`Version` / `AssemblyVersion`).

## Chocolatey packaging

See [pack/chocolatey/README.md](pack/chocolatey/README.md). Package id `sideclip` downloads `Sideclip-win-x64-v0.2.0.zip` from GitHub Releases and installs the agent skill.

## License

[MIT](LICENSE)
