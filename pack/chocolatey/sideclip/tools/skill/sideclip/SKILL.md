---
name: sideclip
description: >
  Open the Sideclip clipboard picker and insert the chosen clip, or insert a
  path from the n most recent Windows screenshots (3 by default, dated text
  list only). Use when the user wants a screenshot path, a clipboard pick in
  this chat, mentions Sideclip, or runs /sideclip.
compatibility: Windows. Picker needs the Sideclip tray app (this build). Screenshot list reads Pictures\Screenshots.
license: MIT
metadata:
  author: kilrkrow
  short-description: Picker or recent screenshot path
---

# Sideclip

Insert a clipboard pick or a screenshot file path into this chat.

Do **not** `read_file` screenshot images. Do **not** copy shots into `images/`. Both dump pixels into context.

## Picker (default for `/sideclip`)

1. Run `powershell -NoProfile -File scripts/sideclip.ps1 pick` from this skill directory (or `sideclip --pick` if on PATH). Set the command wait to at least 10 minutes. It blocks until the user chooses or cancels. Do not run a fallback while it is still running. Do not open a chooser until this command has exited.
2. JSON: `kind` is `image` (print `path`, quote if spaces), `text` (show `text`), or `cancel` (stop).
3. If the command **exits** with an error, Sideclip is not running or is an old build. Say so. Then use the screenshot list below. Do not fake a pick. Do not SendInput.

## Screenshot paths

When the user wants a **file path** from `Pictures\Screenshots` (or the picker failed):

1. `n` = 3 unless they asked for another count.
2. `powershell -NoProfile -File scripts/sideclip.ps1 shots n`
3. If `[]`, stop. Folder missing or empty. Do not invent paths.
4. Print a numbered list: `yyyy-MM-dd HH:mm:ss` filename, full path on the next line.
5. Ask which to insert (question tool if you have one): newest, shot 2/3, or cancel. If they already said newest/latest, insert that path with no chooser.
6. **Insert** = absolute path on its own line, quoted if it contains spaces.

`scripts/sideclip.ps1` lists files without Sideclip.exe. `pick` needs this build of the tray app running.
