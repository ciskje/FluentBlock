# FluentBlock — TODO.md

Activity tracking. Check only what is verified with a green build.
Current version: `0.2.26` (see `VERSION`).

## Version legend

- `X` MAJOR: breaking / stack change (e.g. WPF→WinUI3).
- `Y` MINOR: visible feature (eject, new columns, new language).
- `Z` PATCH: fix, texts, style.

## 0.1.0 — working base (in progress)

Goal: list locks per removable drive + file field, EFIGS menu, Fluent style.

- [x] `AGENT.md` spec approved
- [x] `src/FluentBlock` scaffold (net8.0-windows csproj, App, MainWindow, Models, Services, Themes, EFIGS Resources)
- [x] `DriveEnumerator`: removable letters + label/FS/space
- [x] `HandleEnumerator`: NtQuery + DuplicateHandle + NT→DOS, per-volume filter, async + cancel + timeout
- [x] `FileLockChecker`: Restart Manager for single path
- [x] UI: File/View/Help menu, drive ComboBox, holders DataGrid, Check-file card, StatusBar (state + admin + version)
- [x] Fluent style: corner 4/8, Segoe UI Variable, Mica/DWM fallback, light/dark/system, high-contrast
- [x] Full EFIGS resx (en master, fr/it/de/es same keys), runtime switch + `--lang=`
- [x] `EjectService` stub disabled with "in 0.2.0" tooltip
- [x] Green build with `--artifacts-path C:/Temp/opencode/FluentBlock-artifacts`
- [ ] Manual tests: (a) no-USB empty, (b) USB+Notepad holder, (c) locked file field, (d) 5 languages
- [ ] Tag `v0.1.0`

## 0.1.1 — external USB detection fix (verified)

Goal: E: (USB SSD, `DRIVE_FIXED` + USB BusType) must appear.

- [x] `DriveEnumerator`: include `DRIVE_FIXED` when `IsUsbOrHotplug` (USB BusType via `IOCTL_STORAGE_QUERY_PROPERTY` or `DeviceHotplug` via `IOCTL_STORAGE_GET_HOTPLUG_INFO`)
- [x] Test harness `C:/Temp/opencode/TestDrives`: `count=1`, `E: | COR500 | exFAT`, `C/D=False E=True`
- [x] Dynamic `VersionText` from assembly, `AboutText` 0.1.1 in 5 resx, `VERSION`+csproj `0.1.1`, green rebuild
- [ ] Manual test: open app from `run-debug.bat` and verify visible E: + holder scan
- [ ] Tag `v0.1.1`

## 0.1.5 — movable file card (to verify: build + manual)

Goal: the "Check file" card moves above/below results via header button. (Superseded: replaced by draggable splitter, same version.)

- [x] Draggable splitter between results and file card (instead of the move button); card with minimum height
- [x] Same file open multiple times: single row (first handle)
- [x] Browse: one dialog for files and folders (`BrowseTitle` 5 languages)
- [x] Inline vector icons (refresh/eject/exit/theme/language/about/folder/search/chevron) on buttons, menus, drive list; DWM accent bar; status dot; counts in accent
- [x] Spinner: refresh icon and lens rotate via UI timer during scans, stop + reset to zero in `finally`
- [x] Blue rotating ring in the check-file button during scans (lens still at rest)
- [x] `AboutText` 0.1.5; `VERSION`+csproj `0.1.5`
- [x] Green build + GUI smoke (launch, stays alive, icons/resources ok)
- [x] `dist/`: self-contained single-file release (154 MB) + `VERSION.txt` + `README.txt`, SSD smoke ok
- [x] `installer/`: per-user Inno setup (`FluentBlock.iss` + `build-setup.bat`, dedicated GUID, no UAC, no .NET check) → `FluentBlock-Setup.exe` 49 MB in `dist/`; static verification ok (correct version signature); silent test blocked by SmartScreen (unsigned exe) — first run: "More info" → "Run anyway"
- [ ] Manual test: drag splitter, texts in 2 languages
- [ ] Tag `v0.1.5`

## 0.1.4 — UNC/MUP network path fix (headless verified)

Goal: the file field must find locks on mapped drives (`Z:`) and UNC too.

- [x] Multi-form match: DOS + NT (`QueryDosDevice`) + UNC (`WNetGetUniversalName`) + MUP for file field and drive scan
- [x] MUP names shown as UNC (`\\server\share\...`) instead of `\Device\Mup\...`
- [x] `AboutText` 0.1.4; `VERSION`+csproj `0.1.4`; green build
- [x] Headless harness on `Z:\Lavorazioni\2026\AI\FluentBlock`: COUNT=14 (explorer, opencode, dotnet, cmd, bash) with user
- [ ] Manual test: file field with `Z:` and UNC paths; `Z:` drive (if removable/mapped)
- [ ] Tag `v0.1.4`

## 0.1.3 — repo audit fixes (manual verification pending)

Goal: close the 2026-10-28 audit Must/Should findings with no new features.

- [x] F5 wired to refresh (was an empty binding); single scan at startup (no more double scan)
- [x] `IsUsbOrHotplug` opens `\\.\X:` with zero access (also works non-admin)
- [x] Process name from the already-open handle (`QueryFullProcessImageNameW`), `pid:N` fallback
- [x] Restart Manager under token (30 s also covers RM); identical if/else branch removed
- [x] Light/Dark theme disabled (0.2.0); empty `ApplySystemBackdrop` + `EjectService.IsAvailable` removed; `EjectNotImplemented` in resx
- [x] `RemovableDrive` reduced to Letter+Label (dead FS/space/DevicePath and duplicate P/Invokes gone); list shows label
- [x] `LocksFound`/`RmFallback` in resx (5 languages); dateless `VersionText`; `_cts` disposed
- [x] `AboutText` 0.1.3; `VERSION`+csproj `0.1.3`; green build
- [x] Headless harness on C: (see 0.1.2, repeat after fixes)
- [ ] Manual tests: (a) no-USB empty, (b) USB+Notepad holder+user, (c) file field, (d) 5 languages, F5, non-admin visible E:
- [ ] Tag `v0.1.3`

## 0.1.2 — faster/robust scan from real handle64.exe flow

Goal: faster, robust scan following the real handle64.exe flow
(`FUN_140007a40` class-64 probe, `FUN_140006190` per-PID open, `SetThreadErrorMode`).

- [x] Probe-first: `NtQuerySystemInformation(64, NULL, 0)` for the exact size; explicit localized error (`ScanFailedHandles`) instead of a silent empty list
- [x] One `OpenProcess` per PID (grouped handles, ~200 opens instead of ~120k) + `SetThreadErrorMode(SEM_FAILCRITICALERRORS)` at scan start
- [x] Longest-prefix-first DOS map (fixes `HarddiskVolume1` vs `HarddiskVolume10` collision)
- [x] `NtQueryObject` name with self-growing buffer + 100 ms timeout on a dedicated worker (no pipe hangs; worker always closes dup)
- [x] Real User column via token→SID→`LookupAccountSidW` (reuses the already-open process handle)
- [x] New `ScanProgress`/`ScanFailedHandles` resx keys (5 languages); `AboutText` 0.1.2; `VERSION`+csproj `0.1.2`; green build
- [x] Headless harness on C:: 160316 handles scanned, 3022 File holders, `WITHUSER=3022`, no hangs
- [ ] Manual tests: (b) USB+Notepad holder with visible user, (c) locked file field
- [ ] Tag `v0.1.2`

## 0.2.1 — drag & drop onto the file field

- [x] Drop file/folder anywhere on the window → path into the field + automatic check (`AllowDrop`, copy cursor)
- [x] `AboutText` 0.2.1; `VERSION`+csproj `0.2.1`; green build
- [ ] Manual test: drop files and folders from Explorer (UNC too)
- [ ] Tag `v0.2.1`

## 0.2.0 — ejection (in progress: eject implemented, rest unchanged)

- [x] `EjectService`: bottom-up `CM_Request_Device_EjectW` (disk→parent→grandparent) skipping non-removables, localized veto, confirmation when holders>0, buttons enabled with selected drive, list refresh after eject
- [x] Access column (`GrantedAccess` mask, already collected) + native DataGrid sorting; `ColAccess` resx
- [x] CSV/TSV export (File menu, like `handle -v/-vt`); `MenuExportCsv/Tsv` resx
- [x] Auto-refresh on `WM_DEVICECHANGE` (HwndSource hook, anti-reentrancy guard)
- [x] USB-HDD: `HasRemovableAncestor` (DN_REMOVABLE in chain) as third check in `DriveEnumerator`
- [x] `QueryNameTimeoutMs` configurable via property (20..5000, default 100); UI in 0.3.0 with persistence
- [x] Log `%TEMP%/FluentBlock.log` + Help>Show log; `MenuShowLog` resx
- [x] Harness: populated Access (`0x0012019F`...), C: chain ok, `HasRemovableAncestor(C:)=False`, log written; GUI smoke ok
- [x] Tag `v0.2.0` removed per request (only `v0.2.2` kept); retroactive 0.1.0–0.1.5 tags skipped: single commit, would mislead
- [x] `dist/` realigned to HEAD (re-signed exe+setup+msix)
- [x] `PreviewEjectTarget` for ejection-free diagnostics; Win32 errors captured immediately (no more masked 0x0)
- [x] Blue rotating ring in the check-file button during scans (lens still at rest)
- [x] Eject chain headless-validated on C: (NVMe→controller→root); SetupDi detail-string offset fix (was +8, is +4)
- [x] `EjectOk/Veto/Error/ConfirmTitle/ConfirmText` in resx (5 languages); `AboutText` 0.2.0; `VERSION`+csproj `0.2.0`; green build
- [ ] Manual test: plug E:, implicit preview check, real eject (confirmation with holders, veto with open file)
- [ ] Manual test: CSV/TSV export, Help>log, USB insertion → auto-refresh
- [ ] Tag `v0.2.0`

## 0.2.26 — scans can't stall the UI anymore

- [x] `ScanSelectedDriveAsync` + eject deep-rescan + drive enumeration race their work against a timeout (60 s / 60 s / 30 s): on timeout the UI is freed (spinner stops, status Ready) with a clear `ScanTimeout` message instead of a frozen spinner
- [x] Handle counter back on enumeration (always moves again, like before parallelization)
- [x] Wedged elevated worker is shut down on scan timeout so the next scan starts clean (`ScanTimeout` resx in 5 languages)
- [x] Project renamed FluentSblock → FluentBlock: namespaces/assembly/resx/installer/docs, `src/FluentBlock[.Tests]`, repo `github.com/ciskje/FluentBlock`, local folder, cert `.pfx`/`.cer` filenames (cert subject stays `CN=FluentSblock` until reissued; same Inno AppId so setup upgrades in place; settings auto-migrate once from old `%APPDATA%` path)
- [x] `DrainPending` always terminates under burst wedge: final drain expires wedged queued queries instead of hanging to caller cancellation, no infinite wait when everything billable expires in one sweep (was: 4 min hang to CTS in the golden test under load)
- [x] `ChangelogBody` 0.2.26 entry in 5 languages; `AboutText` 0.2.26; `VERSION`+csproj `0.2.26`
- [ ] Manual test: insert stick on a sick box — within 60 s the UI is usable again with the timeout message
- [x] Tag `v0.2.26`
- [x] Release v0.2.26 (Latest): `dist/` realigned (exe+setup-0.2.26+README+VERSION.txt, no MSIX per request), GitHub release with 2 assets
## 0.2.25 — no more multi-second scan freezes

- [x] Name queries on a shared queue with 8 pool threads: one stuck pipe/minifilter occupies at most all workers while the rest keeps flowing (was: 1 worker, full timeout per stuck handle, frozen counter)
- [x] Owner (`LookupAccountSidW`) resolved lazily only for distinct matched PIDs instead of every scanned PID (slow-DC stalls gone on typical scans)
- [x] Unopenable-PID resolution via a single process-list snapshot (no per-PID `GetProcessById` storms); UNC matcher built on pool thread (no UI-thread network stall)
- [x] `ChangelogBody` 0.2.25 entry in 5 languages; `AboutText` 0.2.25; `VERSION`+csproj `0.2.25`
- [ ] Manual test: counter keeps moving during heavy-AV scan; no 5 s freezes
- [ ] Tag `v0.2.25`

## 0.2.24 — scan elapsed time in log

- [x] `ScanSelectedDriveAsync` + eject deep-rescan log `elapsedMs` (diagnose slow AV filters vs real stalls)
- [x] `ChangelogBody` 0.2.24 entry in 5 languages; `AboutText` 0.2.24; `VERSION`+csproj `0.2.24`
- [ ] Manual test: slow scan shows elapsed ms in Help > Show log
- [ ] Tag `v0.2.24`

## 0.2.23 — eject names hidden blockers (protected processes + deep rescan)

- [x] `DriveScanResult(Holders, Unopenable)`: PIDs that OpenProcess cannot open (protected, e.g. antivirus) are named via the process list; exited PIDs are dropped
- [x] Eject-blocked with 0 holders: deep rescan (elevated worker, 1000 ms name timeout for AV minifilters) + protected list carried through the worker protocol
- [x] `EjectErrorDialog`: red-bulleted protected section (`EjectProtectedTitle/Hint` in 5 languages); targeted remedy when only hidden blockers exist
- [x] `ChangelogBody` 0.2.23 entry in 5 languages; `AboutText` 0.2.23; `VERSION`+csproj `0.2.23`
- [ ] Manual test: eject blocked by antivirus — dialog names the protected program
- [ ] Tag `v0.2.23`

## 0.2.22 — mixed-language UI fix

- [x] Root cause: `L10n.SetLanguage` set only `CurrentUICulture` (per-ExecutionContext), so windows opened by later clicks fell back to the OS language while the main window kept the selected one; now also sets `DefaultThreadCurrentUICulture` (process-wide); `--lang=` routed through `L10n`
- [x] Regression test: `SetLanguagePersistsProcessWide` + `ChangelogBody` 0.2.22 entry in 5 languages; `AboutText` 0.2.22; `VERSION`+csproj `0.2.22`
- [ ] Manual test: switch UI language, open Changelog/About/Eject dialog — all in the selected language
- [ ] Tag `v0.2.22`

## 0.2.21 — wait cursor during eject + EFIGS changelog test

- [x] `OnEjectClick`: `Mouse.OverrideCursor = Cursors.Wait` during the attempt, restored in `finally` (and before the blocked dialog opens, so the dialog shows a normal cursor)
- [x] EFIGS proof: `EfigsTests` — same keys in all 5 resx + changelog keys resolve per UI language (fast, no handles)
- [x] `ChangelogBody` 0.2.21 entry in 5 languages; `AboutText` 0.2.21; `VERSION`+csproj `0.2.21`
- [ ] Manual test: Eject shows the wait cursor; Help > Changelog in 2 languages
- [ ] Tag `v0.2.21`

## 0.2.20 — changelog menu

- [x] Help > Changelog (`ChangelogWindow` with scrollable bullet list, newest first, 0.2.20–0.1.0); `MenuChangelog/ChangelogTitle/ChangelogBody` resx in 5 languages
- [x] `AboutText` 0.2.20; `VERSION`+csproj `0.2.20`; green build + green tests
- [ ] Manual test: Help > Changelog in 2 languages, bullets readable
- [ ] Tag `v0.2.20`

## 0.2.19 — eject blocked dialog names programs + red icon + what-to-do

- [x] `EjectErrorDialog`: red warning icon (`ErrorBrush`), blockers list (`notepad.exe (PID 1234) — E:\file.txt`, grouped by process, max 20) with red bullets; empty-scan case explains temporary blocks
- [x] `OnEjectClick`: fresh (elevated if needed) rescan when holders list is empty so the dialog names the exact programs; blockers passed to the dialog
- [x] Actionable remedies in 5 languages (`EjectVetoRemedy`/`EjectErrorRemedy` as numbered steps) + new `EjectBlockersTitle`/`EjectNoBlockers` keys
- [x] `AboutText` 0.2.19; `VERSION`+csproj `0.2.19`; green build
- [ ] Manual test: open file on E:, Eject → dialog lists the exact program with red icon and steps
- [ ] Tag `v0.2.19`

## 0.2.18 — automatic on-demand elevated worker & UX polish

- [x] Automatic headless elevated worker (`FluentBlock.exe --worker <pipe> <parentPid>`) over Named Pipe: UI always stays at standard-user integrity (no window restart, Explorer OLE drag &amp; drop always works), manual Admin/User switch button removed
- [x] Automatic elevation triggers: file check when user-level scan finds 0 locks, eject when vetoed/insufficient rights, manual Refresh (`F5`) on an empty drive, or click on the status bar indicator
- [x] UX &amp; logic fixes: deduplicate handles by `(Pid, FilePath)` instead of `FilePath` alone; status dot returns to green (`SuccessBrush`) when 0 locks found; `Enter` key in `FilePathBox` and `Browse…` auto-start file check; `FileHoldersGrid` localized with `User`/`Handle` columns + context menu + RM user resolution; `WM_DEVICECHANGE` debounced (350 ms) preserving selected drive; `SaveFileDialog` for CSV/TSV export; side drive list auto-collapses when &lt;= 1 drive
- [x] `AboutText` 0.2.18; `VERSION`+csproj `0.2.18`; green build + green tests
- [x] Manual smoke: Release exe launched, window alive, elevated worker connected on demand (user-closed)
- [x] Tag `v0.2.18`
- [x] Release v0.2.18 (Latest): `dist/` realigned (exe+setup-0.2.18+msix+README+VERSION.txt), GitHub release with 3 assets

## 0.2.17 — start as standard user

- [x] Auto-elevation at startup removed: no more UAC at every launch; elevation is on demand via the Admin/User switch button (`--user` flag retired)
- [x] `AboutText` 0.2.17; `VERSION`+csproj `0.2.17`; green build
- [x] Manual test: launches without UAC as User; switch to Admin and back works
- [x] Tag `v0.2.17`
- [x] Release v0.2.17 (Latest): `dist/` realigned (exe+setup-0.2.17+msix+README+VERSION.txt), GitHub release with 3 assets

## 0.2.16 — Admin/User switch button

- [x] `app.manifest` is `asInvoker` + auto-elevate at startup: starts as Admin by default (UAC at launch); a `requireAdministrator` manifest would re-elevate every launch and make "Switch to User" impossible
- [x] Toolbar button + File menu item with shield icon: restart as User via duplicated Explorer token (`CreateProcessWithTokenW`), `runas /trustlevel` fallback with child-appearance guard (internal `--user` flag opts out of auto-elevation, e.g. for drag &amp; drop) or back as Admin via UAC (`runas`); `ElevationService` preserves CLI args; `SwitchToAdmin/SwitchToUser/RoleSwitchCancelled` resx in 5 languages
- [x] `run-debug.bat` plain launch (UAC prompt comes from auto-elevation)
- [x] `AboutText` 0.2.16; `VERSION`+csproj `0.2.16`; green build
- [x] Elevated drag &amp; drop attempt via `WM_DROPFILES` + message filter (3 messages) with OLE revoked when elevated; Win11 Explorer still delivers nothing (no `drop files=` in log) → best-effort only, supported path is the Paste button; orange hint + `ElevatedDropHint` resx removed
- [x] Paste button: fills the file field from a copied file (`CF_HDROP`) or copied path text — clipboard reads work elevated; `Paste/PasteHint/ClipboardEmpty` resx in 5 languages
- [x] Golden test sets `QueryNameTimeoutMs=2000` (save/restore) against AV/load flakes; app default stays 100 ms (250/500 from the Timeout menu)
- [x] Manual test: starts elevated (UAC at launch); Admin→User stays non-elevated with working drag &amp; drop; User→Admin re-elevates; UAC denied at launch continues as User
- [x] Tag `v0.2.16`
- [x] Release v0.2.16 (Latest): `dist/` realigned (exe+setup-0.2.16+msix+README+VERSION.txt), GitHub release with 3 assets

## 0.2.15 — configurable handle timeout UI

- [x] View>Timeout submenu (50/100/250/500 ms, checkable, persisted, applied at startup); resx `MenuTimeout` 5 languages
- [x] `AppSettings.TimeoutMs` with load fix-up (default 100)
- [x] `AboutText` 0.2.15; `VERSION`+csproj `0.2.15`; green build (smoke N/A: requireAdministrator needs UAC click); commit + tag `v0.2.15` + push; release with assets; `dist/` realigned
- [ ] Tag `v0.2.15`

## 0.2.14 — localized veto types

- [x] 13 `Veto*` resx keys (5 languages) + fallback to raw enum name; cause now fully in UI language
- [x] `AboutText` 0.2.14; `VERSION`+csproj `0.2.14`; build verde; commit + tag `v0.2.14` + push; `dist/` riallineata

## 0.2.13 — dedicated eject error dialog

- [x] `EjectErrorDialog` (warning icon, title, cause, remedy, OK) instead of a status-bar line; structured `EjectResult` from the service
- [x] `EjectDialogTitle/VetoCause/VetoRemedy/ErrorCause/ErrorRemedy` resx 5 languages; dead `EjectVeto` key removed
- [x] `AboutText` 0.2.13; `VERSION`+csproj `0.2.13`; build verde; dialog verificato headless (DIALOG-OK); commit + tag `v0.2.13` + push; `dist/` riallineata

## 0.2.12 — correct veto code + friendly device names

- [x] `CR_REMOVE_VETOED` is `0x17`, not `0x33` (verified in cfgmgr32.h): vetoes now report the reason instead of a cryptic error
- [x] `CM_DRP_DEVICEDESC` is `0x01` (+ `FRIENDLYNAME` `0x0D` first): eject messages name the device properly
- [x] `AboutText` 0.2.12; `VERSION`+csproj `0.2.12`; build verde; commit + tag `v0.2.12` + push; `dist/` riallineata

## 0.2.11 — dedicated About PNG (ICO frame picker doubt)

- [x] `Assets/about-icon.png` (256px source) decoded at exactly 64px, no ICO frame guessing
- [x] `AboutText` 0.2.11; `VERSION`+csproj `0.2.11`; commit + tag `v0.2.11` + push
- [x] Deploy 0.2.11: silent upgrade ok, ARP 0.2.11, `dist/` realigned; installed launch needs UAC click (by design)

## 0.2.10 — sharp About icon

- [x] About icon decodes the 256px frame at exactly 64px (`DecodePixelWidth`) + `HighQuality` scaling (was: wrong frame upscaled, blurry)
- [x] `AboutText` 0.2.10; `VERSION`+csproj `0.2.10`; build verde; commit + tag `v0.2.10` + push; `dist/` riallineata

## 0.2.9 — postinstall launch with admin token

- [x] `[Run]` gets `runascurrentuser`: Inno sheds elevation on postinstall launch by default, which broke against `requireAdministrator` (error 740)
- [x] `AboutText` 0.2.9; `VERSION`+csproj `0.2.9`; build verde; commit + tag `v0.2.9` + push
- [x] `dist/` riallineata (exe+setup-0.2.9+msix); vecchio setup in attesa di sblocco (dialoghi errore aperti)

## 0.2.8 — always elevated at startup

- [x] `app.manifest` with `requireAdministrator` embedded via `ApplicationManifest` (every launch asks UAC)
- [x] `AboutText` 0.2.8; `VERSION`+csproj `0.2.8`; green build; manifest verified in exe; `dist/` realigned; commit + tag `v0.2.8` + push

## 0.2.7 — Program Files default install

- [x] `.iss`: `{pf}`, `admin`, `x64compatible` (app already %APPDATA%/%TEMP%-only, safe)
- [x] Skill `windows-setup` defaulted to Program Files (+ new `inno-programfiles.iss` template)
- [x] `AboutText` 0.2.7; `VERSION`+csproj `0.2.7`; build verde; commit + tag `v0.2.7` + push
- [x] Deploy verificato: silent install in `C:\Program Files\FluentBlock`, ARP HKLM ok, shortcut Start ok, exe installato avviato ok

## 0.2.6 — versioned setup filename

- [x] `OutputBaseFilename=FluentBlock-Setup-{#AppVersion}` + `VersionInfoVersion={#AppVersion}.0` (no more hardcoded versions in `.iss`)
- [x] `build-setup.bat` reads `VERSION` and passes `/DAppVersion=` (single source of truth)
- [x] `AboutText` 0.2.6; `VERSION`+csproj `0.2.6`; green build; commit + tag `v0.2.6` + push
- [x] `dist/` realigned to HEAD; GitHub releases v0.2.2–v0.2.6 (assets on v0.2.6 Latest) + Download section in README

## 0.2.5 — honest tagline + About dialog with big icon

- [x] Tagline "who holds files open?" in AppTitle (5 languages), README, csproj Description
- [x] About dialog with big app icon (64px), name, version, description + OK
- [x] `AboutText` 0.2.5; `VERSION`+csproj `0.2.5`; green build
- [ ] Tag `v0.2.5`

## 0.2.4 — admin-blocked drop warning

- [x] Orange line under the field, shown only on file-field focus while elevated (UIPI blocks the drop itself, focus is the proxy); `ElevatedDropHint` resx 5 languages
- [x] `AboutText` 0.2.4; `VERSION`+csproj `0.2.4`; green build; commit + tag `v0.2.4` + GitHub push

## 0.2.3 — full-width strings in every language (measured review)

- [x] Harness measures real rendered texts (Segoe, system default): clips found on Refresh/Eject/Browse/Who-locks in de/fr/es
- [x] Buttons to `MinWidth` (110/110/100/170) instead of fixed `Width`
- [x] `AboutText` 0.2.3; `VERSION`+csproj `0.2.3`; build verde; commit + tag `v0.2.3`

## 0.2.2 — full English source/docs/specs

- [x] `AGENT.md`, `TODO.md` translated to English
- [x] `dist/LEGGIMI.txt` → `dist/README.txt` (+ `.iss` reference)
- [x] `run-debug.bat` / `installer/build-setup.bat` messages in English
- [x] `AboutText` 0.2.2; `VERSION`+csproj `0.2.2`; green build
- [x] Tag `v0.2.2`; `dist/` (exe+setup+msix+README+VERSION.txt) rebuilt from HEAD
- [x] Doc check: duplicate 0.2.4 section removed, stale lines fixed, tree completed, `dist/` realigned to 0.2.4 (manifest 0.2.4.0)

## 0.3.0 — usability (in progress)

- [x] App icon: code-generated blue padlock, transparent background, 16/32/48/256 PNG-in-ICO; `ApplicationIcon` + `SetupIconFile`
- [x] Holders search/filter (process/file substring), copy path, "Open location" (row menu)
- [x] Persistence: theme/language/columns in `%APPDATA%/FluentBlock/settings.json` (`--lang=` wins); `FilterHint/MenuCopyPath/MenuOpenLocation/MenuAutostart` resx
- [x] Optional autostart (View menu, HKCU Run key) + `FluentBlock` AUMID for taskbar
- [x] Harness: settings roundtrip ok; GUI smoke ok
- [x] Signed MSIX package (`dist/FluentBlock.msix` 67 MB, self-signed `CN=FluentBlock` cert 5 years, generated logos+splash, `installer/FluentBlock.cer`); `signtool verify` ok after trust; setup+exe in `dist/` refreshed to current build
- [ ] User unlock: cert into TrustedPeople (admin) + msix double-click + Start launch
- [ ] Manual test: filter, copy/open, persistent language change, autostart on/off, visible icon

## 1.0.0 — stable

- [x] WinUI 3 migration evaluation: STAY ON WPF. No measurable benefits (already manual Fluent UI, Mica via DWM, per-user xcopy without packaging); WinUI would cost mandatory packaged/MSIX + dropping Windows 10 1809-1909. Revisit only if Store is needed.
- [x] Test suite: `src/FluentBlock.Tests` xUnit, golden fixture vs `handle64.exe -v` (passed: fixture found by both in 24 s)
- [x] Accessibility: `_` mnemonics already in 5 languages, F5 + Ctrl+L field focus, no mouse-only path
- [ ] Accessibility audit (Narrator, contrast, keyboard-only) — manual
- [ ] EFIGS native-speaker review, 5-language user guide — native speakers

## Notes / decisions

- 2026-10-28: WPF .NET8 chosen over WinUI3-packaged for immediate build; isolated `Services/` for future reuse. (v0.1.0)
- 2026-10-28: no handle closing and no kernel driver in v0.x — read-only. (security)
