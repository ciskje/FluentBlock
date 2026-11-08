# FluentSblock — AGENT.md

Specs for coding agents. Source of truth for purpose, architecture, UI, versioning.

## 1. Identity

- Name: `FluentSblock` (working title: "who holds files open?")
- Current version: `0.2.18` — see `VERSION` (single-line `X.Y.Z`).
- Repo/dir: `Z:\Lavorazioni\2026\AI\FluentSblock`.
- Stack v0.1: **C# .NET 8 WPF** (no WinUI3 packaging) with manual Fluent 2 styling.
  Reason: builds with only SDK 8.0.425 installed, no extra workloads, single testable exe.
  Migration to WinUI 3 / WindowsAppSDK can be evaluated in `1.0.0` without changing `Services/`.
- Target OS: Windows 10 1809+ / Windows 11 x64. x64 only (`x64` in csproj).

## 2. Purpose (what it does)

1. For every **removable/ejectable drive** (`DRIVE_REMOVABLE` + `DRIVE_FIXED` on USB/hotplug bus, e.g. SSD in USB enclosure — 0.1.1 fix):
   lists **which processes hold open files** on that volume and therefore block safe removal.
2. Writable field: given an **arbitrary file/folder path**, tells **who holds it locked** (process, PID, user, handle).
3. Standard menu + **EFIGS** languages (en, fr, it, de, es). Default language = Windows UI, fallback `en`.

Does not do: closing other processes' handles (`handle -c` like), installing kernel drivers.
Read-only + safe ejection via system APIs (with confirmation when holders>0).

## 3. Applied Windows 11 UI principles (from `learn.microsoft.com/windows/apps/design/`)

- Design principles: Effortless, Calm, Personal, Familiar, Complete+Coherent.
- Mapped signature experiences:
  - Geometry: windows/dialogs `8px`, controls `4px`, no rounding when maximized/snapped. Tokens in `Themes/FluentTokens.xaml`: `ControlCornerRadius=4`, `OverlayCornerRadius=8`.
  - Layering: base layer (nav/commands) + content layer (results card). No more than 2 levels.
  - Materials: `Mica` backdrop via DWM (`DwmSetWindowAttribute DWMWA_SYSTEMBACKDROP_TYPE=2`) when available, solid `#F3F3F3`/`#202020` fallback.
  - Color: neutral + system `AccentBrush` (DWM, `#0078D4` fallback); 3px accent bar, green/amber/red status dot, counts in accent; light/dark/system + mandatory high-contrast.
  - Type: `Segoe UI Variable` (`Segoe UI` fallback), standard `TextStyles`, no custom fonts.
  - Icons: inline SVG-style vectors (`Path`/`Polygon` on a 24px grid in `Themes/FluentTokens.xaml`, `x:Shared=False`, inherit `Foreground`), zero fonts/dependencies.
  - Motion: implicit WPF animations only, duration ≤167ms, disableable (`SystemParameters`).
  - Input: full mouse+keyboard+touch, `F5` refresh, `Ctrl+L` file-field focus, `Alt+` menus.

## 4. Main window layout (`MainWindow.xaml`)

```
MenuBar: File | View | ?/Help  (File>Refresh,Eject,Export CSV/TSV,Exit; View>Theme,Language,Autostart,Timeout; Help>Show log,About)
Toolbar: [Drive ComboBox] [Refresh F5] [Eject]
Split:
  Left: ListView of removable drives (letter, label; auto-collapses when <= 1 drive or < 700px) — empty state: "No removable drives"
  Right: DataGrid of lock holders (Process, PID, User, Handle, Access, File, sortable, filter box, row menu) — empty state: "No locks"
Bottom card: "Check file" — path TextBox (typable, Enter to check, drag&drop from Explorer always works) + [Browse...] + [Paste: copied file/path] + [Who is locking it?] + results (Process, PID, User, Handle, File + row menu); draggable splitter with results area
StatusBar: scan state, Standard user / Elevated (clickable to pre-start worker) + version
```

Responsive: min 800x500, 2-column `NavigationView`-like layout collapsing to 1 under 700px. All texts from `.resx`, never hardcoded.

## 5. Architecture / files

```
VERSION                         # "0.2.15"
run-debug.bat                   # Debug launcher (asks UAC when not elevated)
README.md                       # GitHub front page (screenshot in docs/)
LICENSE.txt                     # MIT © 2026 Francesco Ferrara
docs/screenshot.png             # main window screenshot for README
screenshot/                     # original user captures (not cropped)
dist/                           # release: Setup.exe + msix + exe + VERSION.txt + README.txt
installer/                      # FluentSblock.iss (Program Files Inno, versioned output) + build-setup.bat (/DAppVersion from VERSION) + FluentSblock.cer (public)
AGENT.md                        # this file
TODO.md                         # activity tracking
src/FluentSblock/
  FluentSblock.csproj           # net8.0-windows, UseWPF, x64, Version 0.2.18
  app.manifest                  # asInvoker: UI stays as User, headless elevated --worker spawned on demand
src/FluentSblock.Tests/
  FluentSblock.Tests.csproj     # xUnit golden: locked fixture vs handle64 -v
  App.xaml[.cs]                 # theme, language, DWM Mica
  AboutWindow.xaml[.cs]         # About dialog with big app icon (dedicated PNG)
  EjectErrorDialog.xaml[.cs]    # eject blocked/failed dialog (title, cause, remedy)
  MainWindow.xaml[.cs]          # layout §4, light MVVM code-behind in v0.1
  Models/
    RemovableDrive.cs           # Letter, Label
    LockHolder.cs               # ProcessName, Pid, User, HandleHex, FilePath, Access
  Services/
    DriveEnumerator.cs          # GetLogicalDrives + GetDriveTypeW + GetVolumeInformationW
    HandleEnumerator.cs         # NtQuerySystemInformation + DuplicateHandle + NtQueryObject (see §6)
    FileLockChecker.cs          # Restart Manager RmStartSession/RegisterResources/GetList (for the file field)
    Log.cs                      # append-only diagnostic log in %TEMP%
    AppSettings.cs              # theme/language/columns in %APPDATA% (JSON, never throws)
    EjectService.cs             # CM_Request_Device_EjectW bottom-up + localized veto + PreviewEjectTarget
    ElevationService.cs         # on-demand headless elevated worker (--worker) over Named Pipe
  Themes/FluentTokens.xaml      # corner radius, brushes, TextStyles, vector icons
  icon.ico                      # blue padlock, transparent bg, 16/32/48/256
  Resources/Strings.{en,fr,it,de,es}.resx  # EFIGS, same keys
```

MVVM: v0.1 code-behind + `ObservableCollection` is enough. Introduce CommunityToolkit.MVVM only if the window exceeds ~400 lines.

## 6. Handle enumeration (rules)

- `EnablePrivilege(SeDebugPrivilege)`. Without admin: lists only own processes, shows "not elevated" banner.
- `SetThreadErrorMode(SEM_FAILCRITICALERRORS)` at scan start (like handle64 main: no dialogs while touching every process).
- `NtQuerySystemInformation(SystemExtendedHandleInformation=64)` via `GetProcAddress(ntdll)`: probe with NULL buffer for the exact size first (like `FUN_140007a40`), then realloc loop on `STATUS_INFO_LENGTH_MISMATCH`. Failure → localized `ScanFailedHandles` error, never a silent empty list. Fallback to `SystemHandleInformation=16`.
- Per PID: `OpenProcess(PROCESS_DUP_HANDLE|QUERY_INFORMATION|QUERY_LIMITED_INFORMATION)`. On failure → skip, not fatal.
- Per handle: `DuplicateHandle(DUPLICATE_SAME_ACCESS)` then `NtQueryObject(ObjectTypeInformation)` — keep only `File`; `NtQueryObject(ObjectNameInformation)` for the NT path (`\Device\HarddiskVolumeN\...`).
- NT→DOS conversion: `QueryDosDeviceW` per letter + cache, longest-prefix-first match; `\Device\Mup\...` converted to UNC `\\server\share...`. For network paths/letters: equivalent forms via `WNetGetUniversalName` (DOS + NT + UNC + MUP all compared).
- Volume filter: DOS path `startswith "E:\"` (case-insensitive). For a single file field: exact match + directory-prefix match.
- User: `OpenProcessToken(TOKEN_QUERY)` on the already-open process handle + `GetTokenInformation(TokenUser)` + `LookupAccountSidW`, fallback `""`, per-PID cache.
- Performance: async scan with `CancellationToken`, `IProgress`, pool-based name queries with 100 ms timeout (avoids pipe hangs). Names/users resolved once per PID from the already-open handle.
- Restart Manager (for `FileLockChecker`): `RmStartSession/RmRegisterResources/RmGetList` — official path, works without `SeDebug` in many cases. Use it for the file field, handle enumeration for volume scans.

Prohibited: no SYS/driver writes, no handle closing, no `Services` keys, no network.

## 7. EFIGS localization

- Keys in `Strings.en.resx` = master. `fr/it/de/es` same keys, full translation before every minor release.
- Date/number formats via `CultureInfo.CurrentUICulture`. Test: launch with `--lang=fr` (override) and verify no visible fallback.
- Standard menus (indicative labels, translate):
  - File: Refresh/Actualiser/Aggiorna/Aktualisieren/Actualizar, Eject, Exit
  - View/Affichage/Visualizza/Ansicht/Vista: Theme, Language/Langue/Lingua/Sprache/Idioma
  - Help/Aide/Aiuto/Hilfe/Ayuda: About/À propos/Informazioni/Info/Acerca de

## 8. X.Y.Z versioning

- File `VERSION`, one line, e.g. `0.1.0`. `csproj.Version` = same value. StatusBar shows same value.
- `MAJOR` (X): breaking (output format change, OS drop, WinUI move).
- `MINOR` (Y): visible feature (eject, kill-handle, languages beyond EFIGS).
- `PATCH` (Z): fix, texts, styling without features.
- Start from `0.1.0`. Every user-visible change → bump + line in `TODO.md` + tag `vX.Y.Z`.
- Pre-1.0: internal APIs free to change, UI stable in EFIGS strings (no key renames without migrating the 5 resx).

## 9. Mandatory agent workflow

- Build/output paths: machine policy. Never commit `bin/obj`.
- Minimum before saying "done": green `dotnet build` + green `dotnet test src/FluentSblock.Tests` + screenshot/file list + `VERSION` matching csproj.
- Release in `dist/`: `dotnet publish src/FluentSblock/FluentSblock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false --artifacts-path C:/Temp/opencode/FluentSblock-publish`, smoke-test the exe per machine policy, then copy exe + `VERSION.txt` + `README.txt` into `dist/` and clean the temp.
- Releases: every `dist/` publish also publishes the GitHub release — `gh release create vX.Y.Z --title "FluentSblock X.Y.Z" --notes "..." dist/FluentSblock-Setup-X.Y.Z.exe dist/FluentSblock.msix dist/FluentSblock.exe`, then `gh release edit vX.Y.Z --latest`. Never leave a pushed tag without its release.
- MSIX: tools from NuGet `Microsoft.Windows.SDK.BuildTools` (makeappx+signtool x64, no admin SDK); private key in `%USERPROFILE%\certs\FluentSblock.pfx` (password in `%USERPROFILE%\certs\password.txt`, NEVER in repo), public in `installer/FluentSblock.cer`; manifest Identity `Version` tracks the release (`X.Y.Z.0`), `CN=FluentSblock`; unpack previous msix for manifest+Assets, swap exe, pack → `signtool sign -fd SHA256` → `signtool verify -pa` (fails until the cert is in TrustedPeople).
- Don't create files beyond §5 without need. Prefer edits over new files.
- Manual tests: (a) no USB → empty state; (b) USB with file open in Notepad → expected row; (c) file field with locked `C:\...` → same PID; (d) switch language to each EFIGS without crash.
- Reply language to the user: Italian. Code comments: English, concise.
- Conversation is Italian, documentation is English: all `.md` files (`AGENT.md`, `TODO.md`, specs, `dist/README.txt`) must be written in English. EFIGS `.resx` translations are product content, not docs.

## 10. Definition of Done v0.1.1

- [x] Green build (machine policy paths)
- [x] Window opens, lists ejectable drives incl. USB-Fixed (E: verified via harness)
- [ ] Window opened from `run-debug.bat` shows E: (user manual test)
- [ ] Refresh shows real holders per volume (verified with open file)
