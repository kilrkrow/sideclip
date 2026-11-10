# Sideclip

Windows tray clipboard that sits beside Win+V. It never steals Win+V.

- Watches the clipboard. Non-secrets persist locally. Secrets stay in memory, expire, and may appear in the picker until wiped (always-secret for 1Password.exe / 1Password-BrowserSupport owner or foreground; browsers use the heuristic).
- Picker: `Ctrl+Shift+Win+V` (type to filter; Enter, Shift+Enter, double-click, and Copy put the selection on the ordinary Windows clipboard only -- no simulated Ctrl+V).
- Screenshot path: `Ctrl+Shift+Win+P` types the newest file in `Pictures\Screenshots`.
- CLI: `sideclip --shots [n]`, `sideclip --newest` (no tray needed), `sideclip --picker`, `sideclip --pick` (tray must be running).
- Agent skill (Grok, Codex, Claude Code): opens the picker (`--pick`) and inserts the chosen clip. Screenshot paths fall back to a dated text list of the last 3 files (no image ingest).
- Release is tray-only. Hotkeys, type-out, and screenshot copy/daily-note options live under tray **Settings...**.
- Optional: copy new screenshots to a folder and append them to today's Obsidian daily note (off by default). Link mode always copies into the vault (never `file:///`); see below.

## Install

### Prerequisite

Sideclip targets `net8.0-windows10.0.19041.0` (Windows 10 1809+ / Server 2019+; OCR needs a Windows OCR language pack) and ships as a framework-dependent win-x64 build. Install the **.NET 8 Desktop Runtime (x64)** before running `Sideclip.exe`:

https://dotnet.microsoft.com/download/dotnet/8.0

If the runtime is missing, the standard .NET host prompt asks you to download it.

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
dotnet publish -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false
```

Output: `bin\Release\net8.0-windows\win-x64\publish\Sideclip.exe`

## Daily use

- **Picker:** `Ctrl+Shift+Win+V` (type to filter). Enter, Shift+Enter, double-click, and Copy place text or images on the ordinary Windows clipboard only. Your own Ctrl+V / Paste remains normal afterward. Sideclip does not simulate Ctrl+V from those actions.
- **Screenshot path:** `Ctrl+Shift+Win+P` types the newest file in `Pictures\Screenshots`.
- Tray icon for history, **Settings...**, TTL, About, and Exit.
- Hotkeys are configurable from **Settings...**. Defaults never bind `Win+V`, `Ctrl+Win+V`, or `Ctrl+Win+P`.


## Screenshots and daily note

Tray **Settings...** > **Screenshots** (all off by default):

1. **Also copy new screenshots to a folder** -- watches the Windows Screenshots known folder and copies each new image once (never moves the original; collisions get ` (1)`, ` (2)`, ...).
2. **Add to Obsidian daily note** -- appends one timestamped line per copied shot to today's note (optional heading, optional OCR callout).

**Embed mode** (copy folder inside the vault): the note gets `![[relative/path.png]]` from that folder. No separate vault-folder setting; the copy folder is reused.

**Link mode** (copy folder outside the vault): Sideclip also copies into a vault-relative folder (default `Attachments/Shots`, created if missing) and writes either:
- **Wikilink** (default): `[[Attachments/Shots/name.png]]`
- **Markdown**: `[shot](Attachments/Shots/name.png)` with spaces and other URL-unsafe characters percent-encoded

The vault folder must be relative (no drive letter, no rooted path, no `..`). Link mode never writes a `file:///` URI. Existing profiles that already used an outside copy folder stay in link mode and get Wikilink + `Attachments/Shots` defaults.
## Type-out (clipboard-free text)

Opt in from tray **Settings...**: enable **Enable clipboard-free text type-out**, optionally set the text-only shortcut (default `Ctrl+Win+Shift+T`), and set bounded delay between chunks (0-250 ms; disabled when type-out is off). Apply saves; Close without Apply leaves prior applied values.

Approved flow (no Arm phase and no tray Go send action):

1. Explicitly select an existing text history entry in the picker (row click or keyboard navigation; merely opening the picker or the initial highlight does not select a source).
2. Focus the destination window and place the caret.
3. Press the configured text-only type-out shortcut once.

Selection alone never types. Type-out never writes the entry to the clipboard and never falls back to Latest/clipboard content. Images, empty/file entries, invalid controls, malformed UTF-16, and wiped secrets are ineligible; choosing an image clears any older text source rather than falling back to it. The trigger is single-use: after an attempted run you must deliberately select again. A trigger with no source is a no-op.

Esc cancels while a pending source exists outside the picker or while a run is active; picker Esc still clears the filter first, then closes and clears the source. Opening Settings, applying changes, turning type-out off, deleting/clearing/expiring the source, or exiting cancels safely. Off -> On and restart never restore a source. Disabled or blank/unassigned releases the shortcut; collisions and reserved chords (`Win+V`, `Ctrl+Win+V`, `Ctrl+Win+P`, and other reserved keys) are refused with an inline error (no hidden fallback).

Live secrets may appear in the picker and be typed while their TTL is live (TTL Off is allowed). Typing does not start, reset, or extend TTL. An expired/deleted/cleared/trimmed source cannot run. Type-out adds no secret payload history, logs, file exports, or CLI/IPC route. CRLF/LF normalize to Enter keystrokes and tabs to Tab keystrokes; other text uses Unicode packets; supplementary characters stay adjacent UTF-16 units. NUL/unsupported controls/malformed UTF-16 are refused. Type-out remains off by default for fresh and legacy settings files.

SendInput is subject to Windows UIPI/integrity-level and focus/target constraints. It cannot promise elevated/secure-desktop/game/RDP/hostile-target compatibility. A bounded chunk after a focus check can still race. Only the selected plain text is sent; there is no macro engine, OCR, image typing, or browser DOM automation.

Tab and Enter are real key presses. The destination decides what they do: a browser or form may move focus on Tab or submit on Enter. The picker shows a visible pre-send warning when the entry contains Tab or Enter/CR/LF. Sideclip stops when the foreground window changes, and also when the focused Win32 control changes in apps that use real child controls. It cannot see focus changes inside a browser page (DOM focus). Completion status reports keys sent and states insertion is not verified. There is no clipboard fallback.

See [type-out test plan and evidence ledger](docs/TYPEOUT-TEST-PLAN.md). Comparison notes versus AutoHotkey SendText and KeePass Auto-Type are in that document (deliberate targeting and cancellation; Sideclip is not a macro host).

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

On Windows, `.\scripts\publish-release.ps1` publishes a framework-dependent win-x64 folder (no bundled runtime, no `.pdb`) to `artifacts\publish\win-x64`, zips `artifacts\Sideclip-win-x64-v0.3.0.zip` (includes `skills/sideclip`), and prints SHA256. Requires the .NET 8 Desktop Runtime (x64) on the machine that runs the app.

## Version

`0.3.0` in `Sideclip.csproj` (`Version` / `AssemblyVersion`). Public metadata uses `kilrkrow`.

## Chocolatey packaging

See [pack/chocolatey/README.md](pack/chocolatey/README.md). Package id `sideclip` downloads `Sideclip-win-x64-v0.3.0.zip` from GitHub Releases and installs the agent skill.

## License

[MIT](LICENSE)

