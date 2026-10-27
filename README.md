# Sideclip

Windows tray clipboard that sits beside Win+V. It never steals Win+V.

- Watches the clipboard. Non-secrets persist locally. Secrets stay in memory and expire (always-secret for 1Password.exe / 1Password-BrowserSupport owner or foreground; browsers use the heuristic).
- Picker: `Ctrl+Shift+Win+V` (type to filter, Enter paste, Shift+Enter copy).
- Screenshot path: `Ctrl+Shift+Win+P` types the newest file in Pictures\Screenshots.
- CLI (tray can be running): `sideclip --shots [n]`, `sideclip --newest`, `sideclip --picker`, `sideclip --pick`.
- Agent skill (Grok / Codex): opens the picker (`--pick`) and inserts the chosen clip. Screenshot paths fall back to a dated text list of the last 3 files (no image ingest).
- Release is tray-only. Hotkeys and the Screenshots options are set from the tray: **Settings...**.
- Screenshots (optional, both off by default): copy every new screenshot to a folder of your choice, and add it to today's Obsidian daily note. See [Screenshots](#screenshots).

## Screenshots

Tray > **Settings...** > **Screenshots**. Aimed at parity with ShareX's "After capture: copy file to folder", without needing ShareX.

**Also copy new screenshots to a folder** (off by default)

- Watches the Windows Screenshots folder (the `Screenshots` known folder, normally `Pictures\Screenshots`, where Win+PrtScn and Snipping Tool save) and copies every new image (png, jpg, jpeg, gif, bmp, webp) to the folder you pick.
- The original is never moved, renamed or deleted. Sideclip waits until the capture tool has finished writing the file.
- Name clashes in the target get ` (1)`, ` (2)`, ... and nothing is ever overwritten. Files that were already there when the option was turned on are not copied.
- The target must exist and cannot be the Screenshots folder itself.

**Add to Obsidian daily note** (off by default, indented under the copy option)

- Only available while the copy option is on with a valid folder. Turning the copy option off greys it out but keeps its values.
- Pick a vault from the list (read from Obsidian's own `%APPDATA%\obsidian\obsidian.json`) or **Browse...** to any vault folder. No Obsidian plugin is needed; Obsidian does not have to be running.
- Uses the vault's core Daily notes settings in `.obsidian/daily-notes.json`: `folder`, `format` (Moment.js tokens, `/` makes subfolders, e.g. `YYYY/MMMM/YYYY-MM-DD`) and `template`. Without that file: `YYYY-MM-DD.md` at the vault root, like Obsidian.
- If today's note does not exist yet it is created, from the template when one is set (`{{title}}`, `{{date}}`, `{{time}}`, `{{date:FORMAT}}`, `{{time:FORMAT}}` are filled in).
- Append only: one line per screenshot, e.g. `- 14:05:32 ![[Attachments/Shots/Screenshot 2026-10-27 140532.png]]`. Existing text is never rewritten; the note's encoding and line endings are kept.
- Copy folder inside the vault: the screenshot is embedded with `![[vault-relative path]]`. Outside the vault: a normal Markdown link to the file (`[name](file:///D:/Shots/name.png)`), and the settings window says so.

**Add OCR text under each screenshot** (off by default, indented under the daily note option)

- Runs the built-in Windows OCR (`Windows.Media.Ocr`, your Windows display languages) on each copy and puts the text in a collapsed callout under the embed:

  ```markdown
  - 14:05:32 ![[Attachments/Shots/Screenshot 2026-10-27 140532.png]]
  > [!note]- OCR text
  > recognised text
  ```

- Background only, with a 15 second limit per image. If OCR fails, times out or finds nothing, the embed line is still written. Copying never waits for it.
- Greyed out if Windows has no OCR language installed.

Problems (folder gone, vault moved, note locked) show one tray notification per kind, not one per screenshot. Nothing here can stop the clipboard watcher.

## Build

Self-contained `win-x64` folder (release / Chocolatey zip):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\Sideclip.exe`

On Windows, `.\scripts\publish-release.ps1` publishes to `artifacts\publish\win-x64`, zips it to `artifacts\Sideclip-win-x64-v0.2.0.zip` (includes `skills/sideclip`), and prints SHA256.

Defaults never bind `Win+V`, `Ctrl+Win+V`, or `Ctrl+Win+P`.

Unit tests (screenshot copier, Moment format, Obsidian config, daily-note append):

```powershell
dotnet test tests\Sideclip.Tests
```

Test hooks (environment variables, all optional):

- `SIDECLIP_DATA_DIR`: use this folder for settings and history instead of `%LocalAppData%\Sideclip`. Runs as a separate instance and never writes the Start with Windows entry.
- `SIDECLIP_SCREENSHOTS_DIR`: watch this folder instead of the Screenshots known folder.
- `SIDECLIP_OBSIDIAN_JSON`: read this vault registry instead of `%APPDATA%\obsidian\obsidian.json`.
- `SIDECLIP_OPEN_SETTINGS=1`: open Settings at startup.

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
