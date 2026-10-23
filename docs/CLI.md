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

- `kind` `image`: includes `path`
- `kind` `text`: includes `text`
- `kind` `cancel`: user closed without a choice
