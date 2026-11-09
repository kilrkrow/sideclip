# Sideclip 0.3.0 type-out test plan

Generic shipped plan for the no-Arm redesign. Evidence folders and machine names belong in private scratch ledgers, not in this file. Version remains 0.3.0. Author identity in public metadata is kilrkrow only. Authored UI strings use ASCII `...`.

## Approved product behavior (source of truth for docs)

1. Tray exposes **Settings...** (single entry for hotkeys and type-out options; no separate Hotkeys dialog and no Arm/Go tray feature menus). Settings is single-instance; closing it leaves the tray running.
2. Settings holds picker / screenshot path / text-only type-out shortcuts, **Enable clipboard-free text type-out**, and bounded delay 0-250 ms (dependent control disabled when off). Blank chord = unassigned. Reserved chords and duplicates are refused inline (no hidden fallback). Defaults never bind Win+V, Ctrl+Win+V, or Ctrl+Win+P.
3. Type-out flow: deliberately select a picker text entry -> focus an external destination -> press the configured text-only shortcut. No Arm button, Ctrl+T arm path, armed phase, or tray Go send action.
4. Enter, Shift+Enter, double-click (row/preview), and Copy put text or images on the ordinary Windows clipboard only. Sideclip does not simulate Ctrl+V there. Ordinary user Ctrl+V / Paste remains normal. Type-out is text-only and clipboard-free.
5. Candidate identity is single-use and separate from Latest/clipboard. Programmatic highlight / filter rebuild / new capture must not silently prime or retarget. Image selection clears an older text candidate. Esc: picker filter-first, then invalidate; global Esc only while pending (outside picker) or running.
6. Completion reports keys sent and states "Insertion not verified." Tab/Enter are real navigation/submit keys. Native HWND/control focus is checked; browser DOM focus is not visible.

## Comparison references (not dependencies)

Consulted official docs (compare deliberate targeting / literal text / cancellation / limitations; do not claim Sideclip implements their full feature sets):

- AutoHotkey v2 Send / SendText: https://www.autohotkey.com/docs/v2/lib/Send.htm -- SendText sends characters literally (Unicode packets); Text mode still maps ``n/`r/`t/`b`` to Enter/Tab/Backspace; Send is a broader keystroke/macro surface than Sideclip.
- KeePass Auto-Type: https://keepass.info/help/base/autotype.html -- explicit sequences, window-title matching, global hotkey, configurable delay, documented UIPI/RDP/layout limits; Sideclip has no sequence language, no window-title matcher, and no privilege elevation path.
- Microsoft RegisterHotKey / MOD_NOREPEAT, SendInput, GetGUIThreadInfo, standard clipboard formats: learn.microsoft.com Win32 docs.

Sideclip intentionally offers one deliberate text source, one configured shortcut, bounded paced chunks, Esc cancel, and honest sent-not-inserted status. No claim of AutoHotkey or KeePass parity.

## Headless unit / source gate (repeatable)

Run on a Windows .NET 8 host with an isolated `SIDECLIP_DATA_DIR` and Autostart=false for the test process. Do not alter unrelated HKCU Run entries.

```powershell
dotnet build Sideclip.csproj -c Release
dotnet test Sideclip.Tests\Sideclip.Tests.csproj -c Release
```

When other builders share the tree, prefer isolated `-p:OutputPath=` / `-p:IntermediateOutputPath=` under a scratch folder inside the worktree so bin/obj races are avoided.

Unit coverage (fake sinks / focus probes; not physical input) is expected to include: UTF-16 order and surrogate integrity, newline/tab normalization, malformed/NUL/control refusal, cancellation between chunks and during delay, partial/zero SendInput reporting, focus/window/control change, stale/wiped/expired secret, single-use trigger, disabled default, settings persistence without priming a source, picker copy-only Enter mapping, native two-control positive and negative focus tests, registration lifetime / collision helpers, and docs/fixture stale-wording guards when present.

## Manual / published-exe acceptance matrix

Use a published self-contained win-x64 build, isolated data dir, synthetic destinations only. No live secrets in screenshots or logs. Record expected vs actual hex or exact UTF-16 where useful.

### A. Clipboard copy (text and image)

| Case | Action | Expect |
| --- | --- | --- |
| text Enter | select text, Enter | clipboard gets exact text once; destination unchanged until user Ctrl+V |
| text Shift+Enter | same | clipboard only |
| text double-click | row or preview | clipboard only |
| text Copy button | click Copy | clipboard only |
| image copy | Enter/double-click/Copy on synthetic PNG | clipboard image; no caption/path typed; older text candidate cleared |
| normal paste | user Ctrl+V into isolated editor / image viewer | exact content or pixel/hash match |

### B. No-Arm type-out

| Case | Steps | Expect |
| --- | --- | --- |
| happy path | deliberate select, leave picker, focus Notepad, shortcut once | exact text once; clipboard sentinel unchanged |
| no source | shortcut with no deliberate select | no-op / status only; no Latest fallback |
| programmatic highlight | open picker / filter rebuild only | no candidate |
| new capture | select text, capture unrelated clip, shortcut | still original text |
| filter away | select, filter removes identity | candidate cleared; row-zero highlight does not replace it |
| Esc cancel | select, Esc (after filter cleared) | candidate cleared |
| image after text | select text, then image | text candidate cleared |
| second press | after one run, press shortcut again | no second send without new select |
| own UI refuse | shortcut while picker/Settings focused | refuse; no type into Sideclip |
| off / unassigned / unavailable | toggle off, blank chord, or registration failure | cannot type; help/status honest |

### C. Six-case Unicode / control matrix (Notepad)

ASCII; CRLF/LF normalization; tabs/quotes; BMP accents/non-Latin; supplementary pairs; mixed CR/tab ordering. Refuse empty/NUL/control/malformed UTF-16/image for type-out.

### D. Fixture browser page

Open `fixtures/paste-blocked.html` in a local browser. Confirm: normal keyboard text accepted; paste/drop blocked; on-page note documents Tab/Enter may leave the field. Do not claim Sideclip reads DOM focus. Save only synthetic readbacks.

### E. Screenshot-path regression

With a test-owned Pictures\Screenshots fixture (no personal photo access), shortcut types the exact quoted newest path. Hash/ownership cleanup afterward.

### F. Native two-control stop

Live two-TextBox (or equivalent native edits): positive stay-in-control completes; switching focused control mid-job stops before later text. Unit positives/negatives already exist; live UI is a separate ledger row.

### G. Registration / Settings

Fresh/legacy default off; enable + Apply registers trigger even with no candidate; blank releases; duplicate/reserved refused; external collision rolls back honestly; reopen Settings raises same instance; Apply cancels prior source; Close without Apply does not silently re-prime.

## Soak plan (not an in-session release gate)

Repeatable longer runs; record OS, destination, expected/actual, timing, status. No live secrets or personal paths in evidence.

1. Long entry with pacing 80 ms; Esc from destination mid-stream; second Esc harmless.
2. Focus switch, destination close, clear/delete/expiry, Settings change, and app exit while pending and while running.
3. Competing / held trigger attempts (document whether MOD_NOREPEAT was requested in source vs physical hold verified).
4. Sleep/wake, RDP, elevated target, keyboard layout change -- mark unverified unless explicitly run.
5. Layout/DPI and tray restart with same isolated data dir: settings persist, never primed.

## Honest evidence ledger template

Fill per integrate / final critic. Do not promote unit or source rows to runtime PASS.

| ID | Claim | Kind | Status | Notes |
| --- | --- | --- | --- | --- |
| U1 | Full `dotnet test` suite green | unit | | raw log path |
| U2 | Native two-control unit +/- | unit | | |
| U3 | Copy-only Enter mapping | unit/source | | |
| S1 | No Arm/Go/Enter-paste in shipped docs | source | | docs/fixture tests |
| S2 | Trigger registration requests MOD_NOREPEAT | source/unit | | physical hold separate |
| R1 | Notepad no-Arm happy path + sentinel | runtime | | |
| R2 | Enter copy-only published exe | runtime | | |
| R3 | Capture does not retarget | runtime | | |
| R4 | Six-case Unicode matrix | runtime | | |
| R5 | Image clipboard paste | runtime | | |
| R6 | paste-blocked.html keyboard vs paste | runtime | | |
| R7 | Screenshot-path quoted type | runtime | | |
| R8 | Live two-control stop | runtime | | |
| R9 | Settings single-instance / collision | runtime | | |
| X1 | Physical Win-key capture in Settings | unverified | leave unverified unless run |
| X2 | Physical held-key repeat | unverified | MOD_NOREPEAT request != physical proof |
| X3 | Browser DOM focus stop | limitation | cannot see DOM; warned |
| X4 | Verified insertion | limitation | status is sent-not-inserted |
| X5 | UIPI / elevated / game / RDP soak | unverified | |

## Packaging recommendations (do not run final zip while parallel builders active)

`scripts\publish-release.ps1 -Version 0.3.0` remains the packaging entrypoint: self-contained win-x64 publish, `artifacts\Sideclip-win-x64-v0.3.0.zip`, print SHA256, skill copy into zip. Recommendations only until integrate + final UI critic:

1. Run publish only after pieces a-d are integrated and the vertical slice re-pass is green.
2. Keep Version 0.3.0 aligned across csproj / zip name / About / Settings title / chocolatey when maintainers bump later -- do not invent a new version policy here.
3. After zip, case-insensitive scan text-bearing artifacts for owner given name/surname; stop on authored hits.
4. Chocolatey VERIFICATION/SHA update is a maintainer step after the GitHub Release asset exists (`pack/chocolatey/README.md`).
5. Prefer isolated `SIDECLIP_DATA_DIR` for any smoke of the published exe; Autostart=false; do not rewrite unrelated HKCU Run values.

## Fixture

`fixtures/paste-blocked.html` is a generic local page: accepts supported keyboard text, blocks paste/drop, and states the Tab/Enter navigation limitation plainly. No network, libraries, selectors product API, or clipboard read.
