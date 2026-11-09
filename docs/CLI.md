# Sideclip CLI

The published exe is a WinExe tray app. These flags attach a console, run, and exit.

```powershell
sideclip --shots [n]     # n newest files in Pictures\Screenshots as JSON (default 3)
sideclip --newest        # newest screenshot path
sideclip --picker        # open the clipboard picker
sideclip --pick          # open the picker; print the chosen clip as JSON
sideclip --help
```

Aliases: `--list` for `--shots`, `--latest` for `--newest`.

## Tray vs no tray

- `--shots` / `--newest` read `Pictures\Screenshots` and work without the tray.
- `--picker` / `--pick` talk to a running tray of this build over a named pipe. If the tray is not running, the CLI prints an error and exits 2.

`--pick` blocks until the user chooses or cancels. `--picker` only asks the tray to open the window.

## `--pick` JSON

- `kind` `image`: includes `path` to a unique temp file (`%TEMP%\sideclip-pick-<guid>.png`). Copy it before exiting if you need it to persist; the tray deletes its pick files on shutdown.
- `kind` `text`: includes `text` for the chosen history entry. This includes a deliberately selected secret entry: the JSON still uses `kind` `text` with that entry's current text, and the tray then removes that secret entry from history. This is the existing `--pick` boundary (unchanged from the 0.3.0 baseline); it is not a type-out export path and docs must not invent a separate secret-return API.
- `kind` `cancel`: user closed the picker without committing a choice (for example scrim/window close), or the tray could not open a picker for the waiter.

## Picker selection vs type-out

`--pick` / `--picker` open the same picker UI. Choosing an entry in the tray picker copies text or images to the ordinary Windows clipboard only (Enter / Shift+Enter / double-click / Copy). Type-out is a separate Settings-enabled flow and is not driven by the CLI flags. `--pick` JSON still returns the chosen text or a temp image path, or `cancel` when no choice was committed.
