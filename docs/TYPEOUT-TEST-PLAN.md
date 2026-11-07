# Sideclip 0.3.0 type-out test plan

## Scope and headless evidence

Headless checks only; the user-facing evidence batch is deferred until the user has consented to app launch/input injection. No Sideclip UI, Notepad, browser, or automated input was launched during the build/test phase.

Windows verification environment: Windows 11, VENGEANCE, .NET 8, PowerShell 5.1.
Build worktree: `D:\_dev\_scratch\sideclip-typeout` (an isolated branch; original checkout preserved).

Commands run:

```powershell
dotnet build Sideclip.csproj -c Release

dotnet test Sideclip.Tests\Sideclip.Tests.csproj -c Release
# 61 passed, 0 failed, 0 skipped
```

The tests use fake sinks and focus probes, not actual input. They verify exact UTF-16 order (including an emoji), newlines/tabs, malformed surrogate rejection, NUL/control rejection, cancellation between chunks and during delay, partial/zero SendInput reporting, focus change, stale/wiped secret, single job, disabled default, legacy/current persistence, and existing picker/screenshot defaults. `ClipEntry` synchronizes payload reads and wipes; selecting or typing does not start a TTL timer or change `CopiedAt`.

## Selected mechanism

Type-out is OFF by default. Tray `Type selected text` -> `On` stores the setting. Its submenu owns pacing (0-250 ms) and the Go assignment hint; `Hotkeys...` edits Go. Go defaults to `Ctrl+Win+Shift+T`, may be cleared while the feature is off, and cannot duplicate picker/screenshot assignments or use reserved `Win+V`, `Ctrl+Win+V`, or `Ctrl+Win+P`.

Select a text entry, click `Arm for type-out`, choose a destination, then press the Go hotkey (or tray status menu -> Go). Arm restores the previous foreground window, does not type, and does not copy. Go and Esc are registered only while armed/running. Registration failure does not arm. Pressing Go twice cannot overlap jobs. `Copy` and Enter/Shift+Enter keep their old clipboard routes; type-out uses no clipboard or `SetTextSuppressing` call.

A bounded batch (at most 16 UTF-16 code units, never splitting a surrogate pair) is sent by `KEYEVENTF_UNICODE`, with `wVk == 0`, down/up per code unit, on a background task. Before each batch it checks cancellation, entry availability/payload, and the exact foreground HWND captured at Go. Entry deletion, clearing, or expiry cancels an armed/running selection. History's own TTL timers are the only TTL clocks. A payload array is cleared after the job; no clipboard, history, CLI, IPC, disk, log, or diagnostic payload copies are added.

CRLF/LF are normalized to Enter keystrokes and tabs to Tab keystrokes. The initial Unicode-only implementation dropped CR/Tab in Notepad; real targets require these key packets. All other text uses Unicode packets. This makes multi-line edits predictable but target keyboard commands are not macros. NUL, unsupported C0 controls and malformed UTF-16 are rejected before sending. Maximum selected text is 1,000,000 UTF-16 units.

## Required manual UI evidence batch (pending)

Run only after warning the user and agreeing where input may go. Set `SIDECLIP_DATA_DIR` to an empty scratch data directory for Sideclip and its terminal before starting it. Use a generic local user, no live credentials, and never capture a secret in a screenshot. Close all unrelated foreground work before injecting keys.

Preparation:
1. Build/publish Release to a scratch folder, keeping the original 0.3.0 icon.
2. Run `Sideclip.exe` from the scratch published folder configured with the isolated data directory. Record initial tooltip and About read-backs: `Sideclip 0.3.0`; `Type selected text` off by default; fresh settings file has TypeOutEnabled false, no armed state, default Go.
3. Place a recorded generic clipboard sentinel in the Windows clipboard. Use scripted clipboard assertions only at clearly marked checkpoints. Do not put any real credential on it.
4. Populate generic text history entries for multi-line, tabs/quotes, accented, and emoji; reproduce marked secret using a generic `correct-horse-battery-staple` heuristic test sample with TTL 8 or 12 seconds. Confirm one image item and a wiped item are not eligible. Persisted history and runtime logs must not include the test secret.

### Acceptance and existing paste/copy
1. Open picker, select a specific multi-line nonsecret, click Enter. Verify exactly one expected paste into Notepad; record clipboard and destination text.
2. Select separate generic text, Shift+Enter. Verify clipboard equals that text without Ctrl+V being sent to Notepad. Shift+Enter/copy semantics remain existing behavior.
3. Select a multi-line entry, click `Arm for type-out`. Wait at least two seconds at an unrelated destination: nothing should be typed in that interval. Place the caret in Notepad manually, then press Go. Compare every character once, in order. Ensure Go and Esc registration occur only from Arm until finish.

### Text matrix

| Case | Expected input | Result / actual | Pass |
| --- | --- | --- | --- |
| newlines | `one\r\ntwo\nthree` normalized to three lines | | |
| tabs/quotes | `indent\tA\t"quoted" separators <>&` | | |
| ASCII | `Ab 09-_=[],./\` | | |
| BMP | `cafe e accent aigu` plus a second accented sample | | |
| supplementary | `A[emoji]B` with a suitable generic emoji | | |
| empty/control | empty, NUL/control, malformed UTF-16 test fixture | no Arm; reason displayed | |

Use `cs` literal encoding for the malformed UTF-16 test rather than saving malformed text in this document. Confirm no split/duplicate surrogate halves. Capture a read-back list of picker Arm eligibility and target-relevant error labels. Read UI text from the same published build rather than just searching source.

### Paste-blocked fixture

Open `fixtures/paste-blocked.html` locally in a portable available viewer/browser approved for this batch. It is generic, local, external-network-free, and its textarea prevents `paste` and simulated `beforeinput paste` while accepting typing/newline/tab.

1. Focus the textarea. Confirm via the page that a normal simulated paste remains blocked and field text stays unchanged.
2. Repeat select -> Arm -> manual target -> Go with tab/newline/emoji test text. Compare output exactly; the fixture's UTF-8 text read-back area should display what arrived. Save only synthetic, secret-free UI evidence.

### Cancellation, TTL, focus, failure, cleanup
1. Long entry: Set pacing 80 ms. Select, Arm, target, Go. As some input arrives press Esc while Notepad (not Sideclip/tray) stays focused. Note wall-clock key time and last subsequent character; wait 3 seconds; nothing further may appear. A second Esc does no harm.
2. Restart long job and close destination, switch foreground to another generic editor, and delete the matching entry or Clear history. Each must stop input/no ghost job. Press End-to-About without input focus; verify status failure contains no payload.
3. Clear selection while armed; ensure no settings/state remains armed after restart and no hotkey is active when idle.
4. Enable a generic synthetic secret, Arm while TTL live, Go, verify output while live. Repeat Arm then expire before Go: unavailable, nothing sent. Repeat Go with long content then expire/clear midstream: no later chunks may appear. Type-out must not extend TTL: use simple read-backs of the TTL row alone; do not save the actual credential in test evidence.
5. Force simulated UIPI/detection failure with an elevated test target only if user explicitly approves the admin interaction (not part of unattended run). Otherwise a controlled fake-sink's failure is headless evidence; explain which case is pending.
6. Exit Sideclip while armed and while running. Wait 3 seconds in recorder; no later characters appear. Reopen from same isolated data dir: off/on persists, Go and delay persist, never armed or running. Tray visible label About 0.3.0, icon present, no console/debug windows. Type chosen hotkeys via the settings dialog; duplicates/reserved examples must be shown as error. Screenshot hotkey remains Ctrl+Shift+Win+P and existing path typing remains unchanged (manual call/site test still pending).

### Data verification checklist
Record SHA256 hashes of `settings.json`, `history.json`, image temp files, and any runtime logs before and after a normal and secret fake-sink/manual job. Read the actual file bodies under scratch and search for controlled synthetic payloads; never screenshot evidentiary file content if the secret is live. Files must not contain a new secret or typed-history copies. Search project/runtime logs for these exact stop literals only in memory, not in a dump: diagnostics must not include typed payloads.

## Limitations / production comparison

Microsoft documents that SendInput is synchronous, serial within one call, does not reset keyboard state, and is subject to UIPI. Therefore elevated targets/secure desktops, games, remote desktops, hostile targets and keyboard-layout/ANSI targets require separate verification and may fail. Type-out does not elevate, focus-grab, use clipboard paste, or promise global compatibility.

A 16-unit chunk is the maximum unstoppable unit; focus can change between its last user32 check and Windows accepting the batch. A foreground HWND match does not prove that a text caret is active. The user must deliberately position the destination; a focus/selection race remains. Keys already held by the user may interfere with Windows. Go releases common Ctrl/Shift/Win modifiers once, consistent with current input infrastructure, not a brute-force focus lock. Esc is a globally registered hotkey only while armed/running; exclusive Esc hotkey collision is surfaced and disallowed.

AutoHotkey SendText uses literal Unicode packets; KeePass Auto-Type supports explicit sequences, target selection, configurable delay, and documented privileges/keyboard-layout limitations. Sideclip intentionally offers only one text entry, explicit Arm/Go, bounded paced chunks, Esc, and no macro language or focus grabbing. No claim of general automation parity is made. Sleep/wake and UIPI/RDP/layout testing is pending manual soak.

## Further soak (not a release gate)

Repeat long entries, cancel at varied offsets, slow apps, remote/elevated targets, keyboard-layout changes, user input during jobs, sleep/wake and tray restart. Record target OS/destination/expected/actual, last-unit timing, status, and any partial-send behavior; no live secrets or private paths in evidence.