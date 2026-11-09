using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FluentBlock.Models;
using FluentBlock.Services;
using Microsoft.Win32;

namespace FluentBlock;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<RemovableDrive> _drives = new();
    private readonly ObservableCollection<LockHolder> _holders = new();
    private readonly ObservableCollection<LockHolder> _fileHolders = new();
    private CancellationTokenSource? _cts;
    private AppSettings.Data _settings = AppSettings.Load();
    private DispatcherTimer? _deviceChangeTimer;

    public MainWindow()
    {
        InitializeComponent();
        // Elevated: OLE DnD is UIPI-blocked AND its registration shadows the
        // WM_DROPFILES fallback (Explorer picks OLE and never sends the
        // message), so revoke it and rely on WM_DROPFILES only.
        // Non-elevated (the default: UI stays as standard user and uses a
        // background worker when elevation is needed) keeps rich OLE drag & drop.
        AllowDrop = !ThemeService.IsElevated();
        if (!string.IsNullOrEmpty(_settings.Lang) && string.IsNullOrEmpty(App.StartupLang))
            L10n.SetLanguage(_settings.Lang);
        ThemeService.ApplyMica(this);
        DriveList.ItemsSource = _drives;
        DriveBox.ItemsSource = _drives;
        DriveBox.DisplayMemberPath = "Display";
        HoldersGrid.ItemsSource = _holders;
        System.Windows.Data.CollectionViewSource.GetDefaultView(_holders).Filter = o =>
            o is LockHolder h && (string.IsNullOrEmpty(_holderFilter) ||
            h.ProcessName.Contains(_holderFilter, StringComparison.OrdinalIgnoreCase) ||
            h.FilePath.Contains(_holderFilter, StringComparison.OrdinalIgnoreCase));
        FileHoldersGrid.ItemsSource = _fileHolders;
        ElevationService.StateChanged += OnElevationStateChanged;
        SizeChanged += (_, _) => UpdateDrivePanelLayout();
        Loaded += async (_, _) => { ApplyTexts(); RestoreColumns(); await RefreshDrivesAsync(); };
        Closing += (_, _) => SaveSettings();
        Closed += (_, _) =>
        {
            ElevationService.StateChanged -= OnElevationStateChanged;
            ElevationService.ShutdownWorker();
        };
        var refreshCommand = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(refreshCommand, (_, _) => OnRefreshClick(this, new RoutedEventArgs())));
        InputBindings.Add(new KeyBinding(refreshCommand, Key.F5, ModifierKeys.None));
        var focusFileCommand = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(focusFileCommand, (_, _) => FilePathBox.Focus()));
        InputBindings.Add(new KeyBinding(focusFileCommand, Key.L, ModifierKeys.Control));
    }

    private void OnElevationStateChanged()
        => Dispatcher.InvokeAsync(UpdateElevationUI);

    private void ApplyTexts()
    {
        Title = L10n.Get("AppTitle");
        MiFile.Header = L10n.Get("MenuFile");
        MiView.Header = L10n.Get("MenuView");
        MiHelp.Header = L10n.Get("MenuHelp");
        MiRefresh.Header = L10n.Get("MenuRefresh");
        MiExportCsv.Header = L10n.Get("MenuExportCsv");
        MiExportTsv.Header = L10n.Get("MenuExportTsv");
        MiEject.Header = L10n.Get("MenuEject");
        MiExit.Header = L10n.Get("MenuExit");
        MiTheme.Header = L10n.Get("MenuTheme");
        MiLanguage.Header = L10n.Get("MenuLanguage");
        MiAbout.Header = L10n.Get("MenuAbout");
        MiAutostart.Header = L10n.Get("MenuAutostart");
        MiAutostart.IsChecked = IsAutostart();
        MiTimeout.Header = L10n.Get("MenuTimeout");
        RefreshTimeoutChecks();
        HandleEnumerator.QueryNameTimeoutMs = _settings.TimeoutMs;
        MiShowLog.Header = L10n.Get("MenuShowLog");
        MiChangelog.Header = L10n.Get("MenuChangelog");
        MiThemeSystem.Header = L10n.Get("ThemeSystem");
        MiThemeLight.Header = L10n.Get("ThemeLight");
        MiThemeDark.Header = L10n.Get("ThemeDark");
        DriveGroup.Header = L10n.Get("DriveGroup");
        HoldersGroup.Header = L10n.Get("HoldersGroup");
        CheckFileGroup.Header = L10n.Get("CheckFileGroup");
        PaintStatus("SuccessBrush");
        RefreshBtnText.Text = L10n.Get("MenuRefresh");
        EjectBtnText.Text = L10n.Get("MenuEject");
        BrowseBtnText.Text = L10n.Get("Browse");
        PasteBtnText.Text = L10n.Get("Paste");
        PasteBtn.ToolTip = L10n.Get("PasteHint");
        CheckFileBtnText.Text = L10n.Get("CheckFileButton");
        FilePathBox.ToolTip = L10n.Get("CheckFileHint");
        ColProcess.Header = L10n.Get("ColProcess");
        ColPid.Header = L10n.Get("ColPid");
        ColUser.Header = L10n.Get("ColUser");
        ColHandle.Header = L10n.Get("ColHandle");
        ColAccess.Header = L10n.Get("ColAccess");
        ColFile.Header = L10n.Get("ColFile");
        FileColProcess.Header = L10n.Get("ColProcess");
        FileColPid.Header = L10n.Get("ColPid");
        FileColUser.Header = L10n.Get("ColUser");
        FileColHandle.Header = L10n.Get("ColHandle");
        FileColFile.Header = L10n.Get("ColFile");
        FilterBox.ToolTip = L10n.Get("FilterHint");
        MiCopyPath.Header = L10n.Get("MenuCopyPath");
        MiOpenLocation.Header = L10n.Get("MenuOpenLocation");
        MiFileCopyPath.Header = L10n.Get("MenuCopyPath");
        MiFileOpenLocation.Header = L10n.Get("MenuOpenLocation");
        StatusText.Text = L10n.Get("StatusReady");
        UpdateElevationUI();
        try { VersionText.Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"; }
        catch { VersionText.Text = "?"; }
        if (_drives.Count == 0 && DriveList.Items.Count == 0)
            ProgressText.Text = L10n.Get("NoDrives");
    }

    private void UpdateDrivePanelLayout()
    {
        bool showSideList = _drives.Count > 1 && ActualWidth >= 700;
        DriveCol.Width = showSideList ? new GridLength(240) : new GridLength(0);
        DriveGroup.Visibility = showSideList ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PaintStatus(string brushKey)
    {
        try { StatusDot.Fill = (Brush)FindResource(brushKey); }
        catch { /* keep current dot */ }
    }

    /// <summary>Rotates button icons on a UI timer until <see cref="StopSpinners"/>.</summary>
    private DispatcherTimer? _spinTimer;

    private void StartSpinners(params RotateTransform[] targets)
    {
        StopSpinners();
        _spinTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Render,
            (_, _) => { foreach (var t in targets) t.Angle = (t.Angle + 21.6) % 360; }, Dispatcher);
        _spinTimer.Start();
    }

    private void StopSpinners(params RotateTransform[] targets)
    {
        _spinTimer?.Stop();
        _spinTimer = null;
        foreach (var t in targets) t.Angle = 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource src)
        {
            src.AddHook(WndProc);
            try
            {
                DragAcceptFiles(src.Handle, true);
                bool dropOk = ChangeWindowMessageFilterEx(src.Handle, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
                bool copyDataOk = ChangeWindowMessageFilterEx(src.Handle, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
                bool copyGlobalOk = ChangeWindowMessageFilterEx(src.Handle, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
                Log.Info($"dropfilter elevated={ThemeService.IsElevated()} ole={AllowDrop} dropfiles={dropOk} copydata={copyDataOk} copyglobal={copyGlobalOk}");
            }
            catch { /* drag & drop stays OLE-only */ }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DEVICECHANGE = 0x0219;
        if (msg == WM_DEVICECHANGE)
        {
            ScheduleDeviceRefresh();
        }
        else if (msg == WM_DROPFILES)
        {
            try { HandleWmDropFiles(wParam); }
            catch { }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ScheduleDeviceRefresh()
    {
        if (_deviceChangeTimer == null)
        {
            _deviceChangeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background,
                async (_, _) =>
                {
                    _deviceChangeTimer?.Stop();
                    await RefreshDrivesAsync(onlyIfChanged: true);
                }, Dispatcher);
        }
        _deviceChangeTimer.Stop();
        _deviceChangeTimer.Start();
    }

    private string _holderFilter = "";

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        _holderFilter = FilterBox.Text.Trim();
        System.Windows.Data.CollectionViewSource.GetDefaultView(_holders).Refresh();
    }

    private LockHolder? SelectedHolder(object? sender)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: DataGrid dg } }
            && dg.SelectedItem is LockHolder fromMenu)
            return fromMenu;
        return (HoldersGrid.IsKeyboardFocusWithin ? HoldersGrid.SelectedItem : null) as LockHolder
            ?? (FileHoldersGrid.IsKeyboardFocusWithin ? FileHoldersGrid.SelectedItem : null) as LockHolder
            ?? HoldersGrid.SelectedItem as LockHolder
            ?? FileHoldersGrid.SelectedItem as LockHolder;
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (SelectedHolder(sender) is { } h)
        {
            try { Clipboard.SetText(h.FilePath); }
            catch { }
        }
    }

    private void OnOpenLocationClick(object sender, RoutedEventArgs e)
    {
        if (SelectedHolder(sender) is { } h)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{h.FilePath}\""); }
            catch (Exception ex) { ProgressText.Text = ex.Message; }
        }
    }

    private void RestoreColumns()
    {
        try
        {
            var cols = new Dictionary<string, DataGridColumn>
            {
                ["Process"] = ColProcess, ["PID"] = ColPid, ["User"] = ColUser,
                ["Handle"] = ColHandle, ["Access"] = ColAccess, ["File"] = ColFile
            };
            foreach (var kv in _settings.Columns)
            {
                if (!cols.TryGetValue(kv.Key, out var col)) continue;
                col.DisplayIndex = kv.Value.DisplayIndex;
                if (kv.Value.Width > 20) col.Width = new DataGridLength(kv.Value.Width);
            }
        }
        catch { /* keep default layout */ }
    }

    private void SaveSettings()
    {
        try
        {
            var cols = new Dictionary<string, AppSettings.ColumnState>();
            foreach (var (key, col) in new (string, DataGridColumn)[]
                     { ("Process", ColProcess), ("PID", ColPid), ("User", ColUser),
                       ("Handle", ColHandle), ("Access", ColAccess), ("File", ColFile) })
                cols[key] = new AppSettings.ColumnState(col.DisplayIndex, col.ActualWidth);
            AppSettings.Save(_settings with { Columns = cols });
        }
        catch { }
    }

    private async Task RefreshDrivesAsync(bool onlyIfChanged = false)
    {
        // Never await enumeration forever: on a wedged device stack the
        // storage IOCTLs may never return. On timeout keep the old list.
        var enumTask = Task.Run(DriveEnumerator.GetRemovableDrives);
        if (await Task.WhenAny(enumTask, Task.Delay(TimeSpan.FromSeconds(30))) != enumTask)
        {
            ProgressText.Text = L10n.Get("ScanTimeout");
            PaintStatus("WarningBrush");
            Log.Info("drives refresh timeout");
            return;
        }
        var fresh = await enumTask;
        if (onlyIfChanged && _drives.SequenceEqual(fresh))
            return;

        string? prevLetter = SelectedLetter();
        _drives.Clear();
        foreach (var d in fresh)
            _drives.Add(d);
        UpdateDrivePanelLayout();

        if (_drives.Count == 0)
        {
            _holders.Clear();
            ProgressText.Text = L10n.Get("NoDrives");
            PaintStatus("SuccessBrush");
            EjectBtn.IsEnabled = false;
            MiEject.IsEnabled = false;
            return;
        }
        EjectBtn.IsEnabled = true;
        MiEject.IsEnabled = true;

        int targetIndex = 0;
        if (prevLetter != null)
        {
            for (int i = 0; i < _drives.Count; i++)
            {
                if (_drives[i].Letter.Equals(prevLetter, StringComparison.OrdinalIgnoreCase))
                {
                    targetIndex = i;
                    break;
                }
            }
        }

        // Set both selections without firing scans, then scan once.
        DriveList.SelectionChanged -= OnDriveSelectionChanged;
        DriveBox.SelectionChanged -= OnDriveSelectionChanged;
        try
        {
            DriveList.SelectedIndex = targetIndex;
            DriveBox.SelectedIndex = targetIndex;
        }
        finally
        {
            DriveList.SelectionChanged += OnDriveSelectionChanged;
            DriveBox.SelectionChanged += OnDriveSelectionChanged;
        }
        await ScanSelectedDriveAsync();
    }

    private string? SelectedLetter()
    {
        if (DriveBox.SelectedItem is RemovableDrive b) return b.Letter;
        if (DriveList.SelectedItem is RemovableDrive d) return d.Letter;
        return _drives.FirstOrDefault()?.Letter;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        // When the user explicitly clicks Refresh on an apparently empty drive
        // while not yet elevated, automatically elevate the worker to inspect
        // system/service processes too.
        bool autoElevate = !ElevationService.IsElevatedOrWorkerActive && _holders.Count == 0;
        await ScanSelectedDriveAsync(forceElevated: autoElevate);
    }

    private async void OnDriveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Keep the two selectors in sync without reentrancy.
        DriveList.SelectionChanged -= OnDriveSelectionChanged;
        DriveBox.SelectionChanged -= OnDriveSelectionChanged;
        try
        {
            if (sender == DriveList && DriveList.SelectedItem is RemovableDrive d)
                DriveBox.SelectedItem = d;
            else if (sender == DriveBox && DriveBox.SelectedItem is RemovableDrive b)
                DriveList.SelectedItem = b;
        }
        finally
        {
            DriveList.SelectionChanged += OnDriveSelectionChanged;
            DriveBox.SelectionChanged += OnDriveSelectionChanged;
        }
        await ScanSelectedDriveAsync();
    }

    private async Task ScanSelectedDriveAsync(bool forceElevated = false)
    {
        var letter = SelectedLetter();
        if (letter == null) return;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var progress = new Progress<string>(s => ProgressText.Text = s);
        StatusText.Text = L10n.Get("StatusScanning");
        PaintStatus("WarningBrush");
        StartSpinners(RefreshSpin);
        RefreshBtn.IsEnabled = false;
        _holders.Clear();
        Log.Info($"scan start drive={letter} forceElevated={forceElevated}");
        var scanSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Never await a scan forever: a wedged kernel call (sick device
            // stack) or a wedged elevated worker would freeze the UI with the
            // spinner running. Abandoned scans keep no UI references: their
            // result is simply never consumed.
            Task<List<LockHolder>> scanTask = (forceElevated || ElevationService.IsElevatedOrWorkerActive)
                ? ScanElevatedOrLocalAsync(letter, progress, ct)
                : HandleEnumerator.GetHoldersForDriveAsync(letter, progress, ct);
            var delayTask = Task.Delay(TimeSpan.FromSeconds(60), ct);
            if (await Task.WhenAny(scanTask, delayTask) != scanTask)
            {
                ct.ThrowIfCancellationRequested(); // superseded by a newer scan
                _cts?.Cancel();
                ElevationService.ShutdownWorker(); // drop a wedged worker so the next scan starts clean
                ProgressText.Text = L10n.Get("ScanTimeout");
                PaintStatus("WarningBrush");
                Log.Info($"scan timeout drive={letter}");
                return;
            }
            var holders = await scanTask;
            foreach (var h in holders) _holders.Add(h);
            ProgressText.Text = holders.Count == 0
                ? L10n.Get("NoLocks")
                : string.Format(L10n.Get("LocksFound"), holders.Count);
            PaintStatus(holders.Count == 0 ? "SuccessBrush" : "WarningBrush");
            Log.Info($"scan done drive={letter} holders={holders.Count} elevated={ElevationService.IsElevatedOrWorkerActive} elapsedMs={scanSw.ElapsedMilliseconds}");
        }
        catch (OperationCanceledException) { Log.Info($"scan cancelled drive={letter} elapsedMs={scanSw.ElapsedMilliseconds}"); }
        catch (Exception ex)
        {
            ProgressText.Text = ex.Message;
            PaintStatus("ErrorBrush");
            Log.Info($"scan fail drive={letter}: {ex.Message}");
        }
        finally
        {
            StopSpinners(RefreshSpin);
            StatusText.Text = L10n.Get("StatusReady");
            RefreshBtn.IsEnabled = true;
            UpdateElevationUI();
        }
    }

    private static async Task<List<LockHolder>> ScanElevatedOrLocalAsync(
        string letter, IProgress<string> progress, CancellationToken ct)
    {
        return await ElevationService.TryScanDriveElevatedAsync(letter, progress, ct)
            ?? await HandleEnumerator.GetHoldersForDriveAsync(letter, progress, ct);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0
            && NoteDrop(paths))
        {
            FilePathBox.Text = paths[0];
            OnCheckFileClick(sender, new RoutedEventArgs());
        }
    }

    private (string Key, DateTime When) _lastDrop;

    private bool NoteDrop(IEnumerable<string> paths)
    {
        string key = string.Join("|", paths);
        var now = DateTime.UtcNow;
        if (_lastDrop.Key == key && (now - _lastDrop.When) < TimeSpan.FromSeconds(2))
            return false;
        _lastDrop = (key, now);
        return true;
    }

    private const int WM_DROPFILES = 0x0233;
    private const int WM_COPYDATA = 0x004A;
    private const int WM_COPYGLOBALDATA = 0x0049;
    private const int MSGFLT_ALLOW = 1;

    private void HandleWmDropFiles(IntPtr hDrop)
    {
        var paths = new List<string>();
        try
        {
            uint n = DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
            var sb = new StringBuilder(32768);
            for (uint i = 0; i < n && i < 64; i++)
            {
                sb.Clear();
                if (DragQueryFileW(hDrop, i, sb, (uint)sb.Capacity) > 0)
                    paths.Add(sb.ToString());
            }
        }
        finally { DragFinish(hDrop); }
        Log.Info($"drop files={paths.Count} first={(paths.Count > 0 ? paths[0] : "-")}");
        if (paths.Count > 0 && NoteDrop(paths))
        {
            FilePathBox.Text = paths[0];
            OnCheckFileClick(this, new RoutedEventArgs());
        }
    }

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, StringBuilder? pszFile, uint cch);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr hDrop);

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);

    private void OnFilePathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnCheckFileClick(sender, new RoutedEventArgs());
        }
    }

    private async void OnCheckFileClick(object sender, RoutedEventArgs e)
    {
        string path = FilePathBox.Text.Trim().Trim('"');
        if (string.IsNullOrEmpty(path)) return;
        // A file answer now beats a drive refresh: cancel any in-flight drive
        // scan so the two full scans don't starve each other (mass expiry).
        _cts?.Cancel();
        _fileHolders.Clear();
        var progress = new Progress<string>(s => ProgressText.Text = s);
        StatusText.Text = L10n.Get("StatusScanning");
        PaintStatus("WarningBrush");
        CheckFileIcon.Visibility = Visibility.Collapsed;
        CheckFileRing.Visibility = Visibility.Visible;
        StartSpinners(CheckFileSpin);
        CheckFileBtn.IsEnabled = false;
        try
        {
            Log.Info($"file check start path={path}");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var holders = await FileLockChecker.WhoLocksAsync(path, progress, cts.Token);
            foreach (var h in holders) _fileHolders.Add(h);
            ProgressText.Text = holders.Count == 0
                ? L10n.Get("NoLocks")
                : string.Format(L10n.Get("LocksFound"), holders.Count);
            PaintStatus(holders.Count == 0 ? "SuccessBrush" : "WarningBrush");
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; PaintStatus("ErrorBrush"); Log.Info($"file check fail: {ex.Message}"); }
        finally
        {
            StopSpinners(CheckFileSpin);
            CheckFileRing.Visibility = Visibility.Collapsed;
            CheckFileIcon.Visibility = Visibility.Visible;
            StatusText.Text = L10n.Get("StatusReady");
            CheckFileBtn.IsEnabled = true;
            UpdateElevationUI();
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        // One dialog for files AND folders: with existence checks off,
        // Open on a highlighted folder returns the folder path.
        var dlg = new OpenFileDialog
        {
            Title = L10n.Get("BrowseTitle"),
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false
        };
        try
        {
            if (SelectedLetter() is string l && Directory.Exists(l + "\\"))
                dlg.InitialDirectory = l + "\\";
        }
        catch { }
        if (dlg.ShowDialog(this) == true)
        {
            FilePathBox.Text = dlg.FileName;
            OnCheckFileClick(sender, e);
        }
    }

    private void OnPasteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count > 0)
                {
                    FilePathBox.Text = files[0];
                    OnCheckFileClick(sender, new RoutedEventArgs());
                    return;
                }
            }
            if (Clipboard.ContainsText())
            {
                string t = Clipboard.GetText().Trim().Trim('"');
                if (!string.IsNullOrEmpty(t))
                {
                    FilePathBox.Text = t;
                    OnCheckFileClick(sender, new RoutedEventArgs());
                    return;
                }
            }
            ProgressText.Text = L10n.Get("ClipboardEmpty");
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
    }

    private async void OnEjectClick(object sender, RoutedEventArgs e)
    {
        var letter = SelectedLetter();
        if (letter == null) return;
        if (_holders.Count > 0)
        {
            var confirm = MessageBox.Show(this,
                string.Format(L10n.Get("EjectConfirmText"), _holders.Count),
                L10n.Get("EjectConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        StatusText.Text = L10n.Get("StatusScanning");
        PaintStatus("WarningBrush");
        Mouse.OverrideCursor = Cursors.Wait; // spinning wheel until eject resolves
        try
        {
            bool wasElevated = ElevationService.IsElevatedOrWorkerActive;
            var result = wasElevated
                ? (await ElevationService.TryEjectElevatedAsync(letter, cts.Token)
                   ?? await EjectService.TryEjectAsync(letter, cts.Token))
                : await EjectService.TryEjectAsync(letter, cts.Token);

            // Automatic elevation when user-level eject fails/vetoes:
            if (!result.Ejected && !wasElevated)
            {
                if (result.VetoType is "InsufficientRights" or null)
                {
                    var elevatedResult = await ElevationService.TryEjectElevatedAsync(letter, cts.Token);
                    if (elevatedResult != null) result = elevatedResult;
                }
            }

            if (result.Ejected)
            {
                ProgressText.Text = L10n.Get("EjectOk");
                PaintStatus("SuccessBrush");
                Log.Info($"eject drive={letter} ok");
                await RefreshDrivesAsync();
                return;
            }

            // Blocked with an empty holders list: rescan deeper (elevated,
            // longer name-query timeout for AV minifilters) and also collect
            // processes whose handles cannot be inspected (e.g. antivirus).
            List<HandleEnumerator.SkippedProcess> hidden = [];
            if (_holders.Count == 0)
            {
                int prevTimeout = HandleEnumerator.QueryNameTimeoutMs;
                HandleEnumerator.QueryNameTimeoutMs = 1000;
                try
                {
                    using var scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var scanProgress = new Progress<string>(s => ProgressText.Text = s);
                    var deepSw = System.Diagnostics.Stopwatch.StartNew();
                    // Same 60 s cap as drive scans: a wedged kernel call must
                    // not freeze the eject flow (and its wait cursor) forever.
                    Task<HandleEnumerator.DriveScanResult> deepScanTask = RunDeepScanAsync();
                    async Task<HandleEnumerator.DriveScanResult> RunDeepScanAsync() =>
                        await ElevationService.TryScanDriveDetailedElevatedAsync(letter, scanProgress, scanCts.Token)
                        ?? await HandleEnumerator.GetDriveScanDetailedAsync(letter, scanProgress, scanCts.Token);
                    var deepTimeout = Task.Delay(TimeSpan.FromSeconds(60), scanCts.Token);
                    if (await Task.WhenAny(deepScanTask, deepTimeout) != deepScanTask)
                    {
                        scanCts.Cancel();
                        ElevationService.ShutdownWorker();
                        ProgressText.Text = L10n.Get("ScanTimeout");
                        PaintStatus("WarningBrush");
                        Log.Info($"eject deep-scan timeout drive={letter}");
                        return;
                    }
                    var detailed = await deepScanTask;
                    _holders.Clear();
                    foreach (var h in detailed.Holders) _holders.Add(h);
                    hidden = detailed.Unopenable;
                    ProgressText.Text = _holders.Count == 0
                        ? L10n.Get("NoLocks")
                        : string.Format(L10n.Get("LocksFound"), _holders.Count);
                    PaintStatus(_holders.Count == 0 ? "SuccessBrush" : "WarningBrush");
                    Log.Info($"eject deep-scan drive={letter} holders={_holders.Count} hidden={hidden.Count} elapsedMs={deepSw.ElapsedMilliseconds}");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { ProgressText.Text = ex.Message; }
                finally { HandleEnumerator.QueryNameTimeoutMs = prevTimeout; }
            }
            var blockers = _holders.ToList();
            string cause, remedy;
            if (result.VetoType != null)
            {
                cause = string.Format(L10n.Get("EjectVetoCause"), VetoDisplayName(result.VetoType), result.VetoName);
                remedy = L10n.Get("EjectVetoRemedy");
            }
            else
            {
                cause = string.Format(L10n.Get("EjectErrorCause"), result.ErrorCode);
                remedy = L10n.Get("EjectErrorRemedy");
            }
            ProgressText.Text = cause;
            PaintStatus("ErrorBrush");
            Log.Info($"eject drive={letter} blocked: {cause} blockers={blockers.Count} hidden={hidden.Count}");
            Mouse.OverrideCursor = null; // normal cursor while the user reads the dialog
            new EjectErrorDialog(L10n.Get("EjectDialogTitle"), cause, remedy, blockers, hidden) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; PaintStatus("ErrorBrush"); }
        finally
        {
            Mouse.OverrideCursor = null;
            StatusText.Text = L10n.Get("StatusReady");
            UpdateElevationUI();
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateElevationUI()
    {
        bool admin = ElevationService.IsElevatedOrWorkerActive;
        AdminText.Text = admin ? L10n.Get("Elevated") : L10n.Get("NotElevated");
        AdminStatusItem.Cursor = admin ? Cursors.Arrow : Cursors.Hand;
    }

    private async void OnAdminStatusClick(object sender, MouseButtonEventArgs e)
    {
        if (ElevationService.IsElevatedOrWorkerActive) return;
        if (await ElevationService.EnsureWorkerAsync())
        {
            UpdateElevationUI();
            await ScanSelectedDriveAsync(forceElevated: true);
        }
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        MiThemeSystem.IsChecked = sender == MiThemeSystem;
        MiThemeLight.IsChecked = sender == MiThemeLight;
        MiThemeDark.IsChecked = sender == MiThemeDark;
        _settings = _settings with
        {
            Theme = sender == MiThemeLight ? "light" : sender == MiThemeDark ? "dark" : "system"
        };
        SaveSettings();
    }

    private async void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string lang)
        {
            L10n.SetLanguage(lang);
            _settings = _settings with { Lang = lang };
            SaveSettings();
            ApplyTexts();
            await RefreshDrivesAsync();
        }
    }

    private static string VetoDisplayName(string veto)
    {
        string key = "Veto" + veto;
        string s = L10n.Get(key);
        return s == key ? veto : s;
    }

    private void OnShowLogClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(Log.FilePath))
                Log.Info("FluentBlock log started");
            System.Diagnostics.Process.Start("notepad.exe", Log.FilePath);
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e) => ExportHolders(",", false);

    private void OnExportTsvClick(object sender, RoutedEventArgs e) => ExportHolders("\t", true);

    private void ExportHolders(string sep, bool tsv)
    {
        if (_holders.Count == 0) { ProgressText.Text = L10n.Get("NoLocks"); return; }
        var dlg = new SaveFileDialog
        {
            OverwritePrompt = true,
            CheckPathExists = true,
            ValidateNames = true,
            Filter = tsv ? "TSV (*.tsv)|*.tsv" : "CSV (*.csv)|*.csv",
            FileName = tsv ? "holders.tsv" : "holders.csv"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(sep, "Process", "PID", "User", "Handle", "Type", "Access", "File"));
            foreach (var h in _holders)
            {
                string Cell(string s) => tsv ? s.Replace("\t", " ") : (s.Contains('"') || s.Contains(',') ? '"' + s.Replace("\"", "\"\"") + '"' : s);
                sb.AppendLine(string.Join(sep, Cell(h.ProcessName), h.Pid.ToString(), Cell(h.User),
                    h.HandleHex, "File", h.Access, Cell(h.FilePath)));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            ProgressText.Text = dlg.FileName;
            Log.Info($"exported {_holders.Count} holders to {dlg.FileName}");
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
    }

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        SetAutostart(!IsAutostart());
        MiAutostart.IsChecked = IsAutostart();
    }

    private static bool IsAutostart()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return k?.GetValue("FluentBlock") != null;
        }
        catch { return false; }
    }

    private void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (k == null) return;
            if (on)
                k.SetValue("FluentBlock", "\"" + Environment.ProcessPath + "\"");
            else
                k.DeleteValue("FluentBlock", false);
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
    }

    private void RefreshTimeoutChecks()
    {
        foreach (var item in MiTimeout.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is string s && s == _settings.TimeoutMs.ToString();
    }

    private void OnTimeoutClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string s && int.TryParse(s, out int ms))
        {
            HandleEnumerator.QueryNameTimeoutMs = ms;
            _settings = _settings with { TimeoutMs = HandleEnumerator.QueryNameTimeoutMs };
            SaveSettings();
            RefreshTimeoutChecks();
        }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
        => new AboutWindow { Owner = this }.ShowDialog();

    private void OnChangelogClick(object sender, RoutedEventArgs e)
        => new ChangelogWindow { Owner = this }.ShowDialog();
}
