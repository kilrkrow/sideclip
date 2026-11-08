# Sideclip

Windows tray clipboard that sits beside Win+V. It never steals Win+V.

- Watches the clipboard. Non-secrets persist locally. Secrets stay in memory, expire, and may appear in the picker until wiped (always-secret for 1Password.exe / 1Password-BrowserSupport owner or foreground; browsers use the heuristic).
- Picker: `Ctrl+Shift+Win+V` (type to filter, Enter paste, Shift+Enter copy).
- Screenshot path: `Ctrl+Shift+Win+P` types the newest file in `Pictures\Screenshots`.
- CLI: `sideclip --shots [n]`, `sideclip --newest` (no tray needed), `sideclip --picker`, `sideclip --pick` (tray must be running).
- Agent skill (Grok, Codex, Claude Code): opens the picker (`--pick`) and inserts the chosen clip. Screenshot paths fall back to a dated text list of the last 3 files (no image ingest).
- Release is tray-only. Hotkeys are assignable from the tray.

## Install

### GitHub Releases

Download `Sideclip-win-x64-v0.3.0.zip` from [Releases](https://github.com/kilrkrow/sideclip/releases/latest), extract it, and run `Sideclip.exe`.

The zip also includes `skills/sideclip` for a manual skill copy.

### Chocolatey

The `sideclip` 0.3.0 package has been submitted to the Chocolatey community feed and is in moderation. Once it is approved:

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

## Type-out instead of paste

Opt in from tray `Type selected text` -> `On`. Select a non-empty text history entry, click `Arm for type-out`, move the caret into the destination, and explicitly press Go (default `Ctrl+Win+Shift+T`). This never writes the entry to the clipboard. Esc cancels while the job is armed or typing; changing windows stops subsequent chunks. Go and pacing live in the existing tray settings/hotkey UI, persist across restarts, and a restart never preserves an armed job. Images, files/empty entries, invalid control/malformed UTF-16, and wiped secrets are ineligible.

Live secrets may appear in the picker and be typed while their TTL is live. Typing does not start, reset, or extend their existing TTL timer. An expired/deleted/cleared source stops the job, with liveness and foreground handled checked before each chunk. Type-out adds no secret payload history, logs, file exports, or CLI/IPC route. CRLF/LF normalize to Enter keystrokes and tabs to Tab keystrokes for real newline/indentation behavior; other text uses Unicode packets and supplementary characters remain adjacent UTF-16 surrogate code units. NUL/unsupported controls/malformed UTF-16 are refused. `Type selected text` remains off by default for old settings files.

SendInput is subject to Windows UIPI/integrity-level and focus/target constraints. It cannot promise elevated/secure-desktop/game/RDP/hostile-target compatibility. A bounded chunk after a focus check can still have a small unavoidable race, and no global text-caret claim is made. Only the selected plain text is sent; no macro engine or generic automation is introduced.

Tab and Enter are real key presses, not text. The destination decides what they do: a browser or form may move focus on Tab or submit on Enter, and the rest of the entry then lands somewhere else or nowhere. In a Microsoft Edge textarea, typing `tab<Tab>here<LF>next` produced `tabe<LF>next` in one run and `tabhere<LF>next` in another: Tab moved focus, so it never landed in the field. The picker warns before you arm an entry that has Tab or Enter/CR/LF, and the armed status repeats the warning. Sideclip stops when the foreground window changes, and also when the focused Win32 control changes in apps that use real child controls. It cannot see focus changes inside a browser page (DOM focus). So the final status reads `Type-out sent N of N keys. Insertion not verified.`: the keys were sent, but nothing checks what the target inserted. There is no clipboard fallback and no browser automation.

See [type-out test plan and manual evidence batch](docs/TYPEOUT-TEST-PLAN.md).

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

After GitHub release `v0.3.0`, submit a remote catalog entry to [xai-org/plugin-marketplace](https://github.com/xai-org/plugin-marketplace) pointing at `https://github.com/kilrkrow/sideclip.git` (pinned SHA), so Grok `/marketplace` can list it.

## Build and release zip

On Windows, `.\scripts\publish-release.ps1` publishes to `artifacts\publish\win-x64`, zips `artifacts\Sideclip-win-x64-v0.3.0.zip` (includes `skills/sideclip`), and prints SHA256.

## Version

`0.3.0` in `Sideclip.csproj` (`Version` / `AssemblyVersion`).

## Chocolatey packaging

See [pack/chocolatey/README.md](pack/chocolatey/README.md). Package id `sideclip` downloads `Sideclip-win-x64-v0.3.0.zip` from GitHub Releases and installs the agent skill.

## License

[MIT](LICENSE)
