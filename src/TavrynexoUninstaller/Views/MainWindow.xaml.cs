using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Tavrynexo.Core;
using Tavrynexo.Models;
using Tavrynexo.Services;

namespace Tavrynexo.Views;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<InstalledProgram> _programs = new();
    private readonly ObservableCollection<InstalledProgram> _store = new();
    private readonly ObservableCollection<StartupEntry> _startup = new();
    private readonly ObservableCollection<JunkCategory> _junk = new();

    private readonly ICollectionView _programsView;
    private readonly ICollectionView _storeView;

    private string _query = "";
    private string _storeQuery = "";
    private string _sortProp = "Name";
    private ListSortDirection _sortDir = ListSortDirection.Ascending;

    private bool _busy;
    private bool _storeLoaded, _startupLoaded, _junkLoaded;
    private bool _settingsReady;

    public MainWindow()
    {
        InitializeComponent();
        FlowDirection = L.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        ProgramList.ItemsSource = _programs;
        StoreList.ItemsSource = _store;
        StartupList.ItemsSource = _startup;
        JunkList.ItemsSource = _junk;

        _programsView = CollectionViewSource.GetDefaultView(_programs);
        _programsView.Filter = o => o is InstalledProgram p && Matches(p, _query);
        _storeView = CollectionViewSource.GetDefaultView(_store);
        _storeView.Filter = o => o is InstalledProgram p && Matches(p, _storeQuery);

        VersionText.Text = L.F("Version", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

        Loaded += async (_, _) =>
        {
            LoadSettingsUi();
            ShowView(ViewPrograms);
            await LoadProgramsAsync();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.UseDarkTitleBar(this);
    }

    // ===================== Helpers =====================

    private static bool Matches(InstalledProgram p, string q) =>
        string.IsNullOrWhiteSpace(q) ||
        p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
        p.Publisher.Contains(q, StringComparison.CurrentCultureIgnoreCase);

    private void ShowBusy(string text)
    {
        _busy = true;
        BusyText.Text = text;
        BusyOverlay.Visibility = Visibility.Visible;
    }

    private void SetBusy(string text) => BusyText.Text = text;

    private void HideBusy()
    {
        _busy = false;
        BusyOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowView(UIElement view)
    {
        foreach (var v in new UIElement[] { ViewPrograms, ViewStore, ViewStartup, ViewJunk, ViewSettings })
            v.Visibility = ReferenceEquals(v, view) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        try
        {
            if (ReferenceEquals(sender, NavPrograms)) ShowView(ViewPrograms);
            else if (ReferenceEquals(sender, NavStore))
            {
                ShowView(ViewStore);
                if (!_storeLoaded) await LoadStoreAsync();
            }
            else if (ReferenceEquals(sender, NavStartup))
            {
                ShowView(ViewStartup);
                if (!_startupLoaded) await LoadStartupAsync();
            }
            else if (ReferenceEquals(sender, NavJunk))
            {
                ShowView(ViewJunk);
                if (!_junkLoaded) await AnalyzeJunkAsync();
            }
            else ShowView(ViewSettings);
        }
        catch (Exception ex) { Logger.Error("Nav", ex); }
    }

    // ===================== Programs =====================

    private async Task LoadProgramsAsync()
    {
        ShowBusy(L.Get("Loading"));
        try
        {
            var list = await Task.Run(ProgramScanner.Scan);
            _programs.Clear();
            foreach (var p in list) _programs.Add(p);
            ApplySort();
            UpdateProgramsCount();
            _ = ProgramScanner.LoadIconsAndSizesAsync(list)
                .ContinueWith(_ => Dispatcher.Invoke(UpdateProgramsCount));
        }
        catch (Exception ex)
        {
            Logger.Error("Load programs", ex);
            Dialogs.Error(this, ex.Message);
        }
        finally { HideBusy(); }
    }

    private void UpdateProgramsCount()
    {
        var shown = _programsView.Cast<InstalledProgram>().ToList();
        ProgramsCountText.Text = L.F("ProgramsCount", shown.Count, Fmt.Size(shown.Sum(p => p.SizeBytes)));
    }

    private void ApplySort()
    {
        _programsView.SortDescriptions.Clear();
        _programsView.SortDescriptions.Add(new SortDescription(_sortProp, _sortDir));
        if (_sortProp != "Name")
            _programsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
    }

    private void SortHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string prop }) return;
        if (_sortProp == prop)
            _sortDir = _sortDir == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        else
        {
            _sortProp = prop;
            _sortDir = prop is "SizeBytes" or "InstallDate" ? ListSortDirection.Descending : ListSortDirection.Ascending;
        }
        ApplySort();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text.Trim();
        _programsView.Refresh();
        UpdateProgramsCount();
    }

    private async void RefreshPrograms_Click(object sender, RoutedEventArgs e) => await LoadProgramsAsync();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramList.SelectedItem is not InstalledProgram p) { Dialogs.Info(this, L.Get("SelectFirst")); return; }
        OpenFolderOf(p);
    }

    private void ProgramList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProgramList.SelectedItem is InstalledProgram p && e.OriginalSource is FrameworkElement) OpenFolderOf(p);
    }

    private void OpenFolderOf(InstalledProgram p)
    {
        if (!string.IsNullOrEmpty(p.InstallLocation) && Directory.Exists(p.InstallLocation))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{p.InstallLocation}\"") { UseShellExecute = true });
        else
            Dialogs.Info(this, L.Get("NoFolder"));
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e) => await SafeAsync(() => UninstallSelectedAsync(false));
    private async void Force_Click(object sender, RoutedEventArgs e) => await SafeAsync(() => UninstallSelectedAsync(true));

    private async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            Logger.Error("Action failed", ex);
            HideBusy();
            Dialogs.Error(this, ex.Message);
        }
    }

    private async Task UninstallSelectedAsync(bool force)
    {
        if (_busy) return;

        var selected = ProgramList.SelectedItems.Cast<InstalledProgram>().ToList();
        if (selected.Count == 0) { Dialogs.Info(this, L.Get("SelectFirst")); return; }

        var names = string.Join("\n", selected.Select(p => "•  " + p.Name));
        if (!Dialogs.Confirm(this, L.F(force ? "ConfirmForce" : "ConfirmUninstall", names), force)) return;

        if (!force)
        {
            var broken = selected.Where(p => !p.HasUninstaller).ToList();
            foreach (var b in broken) Dialogs.Warn(this, L.F("NoUninstaller", b.Name));
            selected = selected.Except(broken).ToList();
            if (selected.Count == 0) return;
        }

        ShowBusy(L.Get("Loading"));
        var leftovers = new List<LeftoverItem>();
        int scanned = 0;

        try
        {
            if (AppSettings.Current.CreateRestorePoint)
            {
                SetBusy(L.Get("CreatingRestore"));
                var label = selected.Count == 1 ? selected[0].Name : $"{selected.Count} programs";
                await Task.Run(() => RestorePointService.Create("Tavrynexo: " + label));
            }

            var snapshot = _programs.ToList();

            foreach (var p in selected)
            {
                if (!force)
                {
                    SetBusy(L.F("Uninstalling", p.Name));
                    var r = await Task.Run(() => UninstallService.Run(p));
                    if (!r.Started)
                    {
                        Dialogs.Error(this, $"{p.Name}\n\n{r.Error}");
                        continue;
                    }

                    // Protect the user's files: if the program is still registered, the uninstall was cancelled / failed.
                    if (UninstallService.IsStillInstalled(p))
                    {
                        Dialogs.Warn(this, L.F("StillInstalled", p.Name));
                        continue;
                    }
                }

                SetBusy(L.F("Scanning", p.Name));
                var found = await Task.Run(() => LeftoverScanner.Scan(p, snapshot));
                scanned++;
                leftovers.AddRange(found);
            }
        }
        finally { HideBusy(); }

        if (scanned > 0)
        {
            if (leftovers.Count == 0)
                Dialogs.Info(this, L.Get("NoLeftovers"));
            else
            {
                var label = selected.Count == 1 ? selected[0].Name : $"{selected.Count}_programs";
                var win = new LeftoverWindow(leftovers, label) { Owner = this };
                win.ShowDialog();
            }
        }

        await LoadProgramsAsync();
    }

    // ===================== Store apps =====================

    private async Task LoadStoreAsync()
    {
        ShowBusy(L.Get("Loading"));
        try
        {
            var list = await Task.Run(StoreAppService.Scan);
            _store.Clear();
            foreach (var p in list) _store.Add(p);
            _storeLoaded = true;
            StoreCountText.Text = L.F("ProgramsCount", _store.Count, "");
            StoreCountText.Text = StoreCountText.Text.TrimEnd(' ', '·');
        }
        finally { HideBusy(); }
    }

    private void StoreSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _storeQuery = StoreSearchBox.Text.Trim();
        _storeView.Refresh();
    }

    private async void RefreshStore_Click(object sender, RoutedEventArgs e) => await SafeAsync(LoadStoreAsync);

    private async void RemoveStore_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        if (_busy) return;
        var selected = StoreList.SelectedItems.Cast<InstalledProgram>().ToList();
        if (selected.Count == 0) { Dialogs.Info(this, L.Get("SelectFirst")); return; }

        var names = string.Join("\n", selected.Select(p => "•  " + p.Name));
        if (!Dialogs.Confirm(this, L.F("ConfirmStore", names), true)) return;

        ShowBusy(L.Get("Loading"));
        var errors = new List<string>();
        try
        {
            if (AppSettings.Current.CreateRestorePoint)
            {
                SetBusy(L.Get("CreatingRestore"));
                await Task.Run(() => RestorePointService.Create("Tavrynexo: Store apps"));
            }

            foreach (var p in selected)
            {
                SetBusy(L.F("RemovingStore", p.Name));
                var (ok, err) = await Task.Run(() => StoreAppService.Remove(p));
                if (!ok) errors.Add(L.F("StoreFailed", p.Name, err));
            }
        }
        finally { HideBusy(); }

        foreach (var er in errors) Dialogs.Error(this, er);
        await LoadStoreAsync();
    });

    // ===================== Startup =====================

    private async Task LoadStartupAsync()
    {
        var list = await Task.Run(StartupService.Scan);
        _startup.Clear();
        foreach (var s in list) _startup.Add(s);
        _startupLoaded = true;
    }

    private async void RefreshStartup_Click(object sender, RoutedEventArgs e) => await SafeAsync(LoadStartupAsync);

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: StartupEntry entry } cb) return;
        try { StartupService.SetEnabled(entry, cb.IsChecked == true); }
        catch (Exception ex)
        {
            Logger.Error("Startup toggle", ex);
            cb.IsChecked = entry.Enabled;
            Dialogs.Error(this, ex.Message);
        }
    }

    private async void StartupDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: StartupEntry entry }) return;
        if (!Dialogs.Confirm(this, L.F("ConfirmDeleteEntry", entry.Name), true)) return;
        try
        {
            await Task.Run(() => StartupService.Delete(entry));
            _startup.Remove(entry);
        }
        catch (Exception ex)
        {
            Logger.Error("Startup delete", ex);
            Dialogs.Error(this, ex.Message);
        }
    }

    // ===================== Junk cleaner =====================

    private async Task AnalyzeJunkAsync()
    {
        ShowBusy(L.Get("Loading"));
        try
        {
            _junk.Clear();
            foreach (var c in JunkService.BuildCategories()) _junk.Add(c);
            var cats = _junk.ToList();
            await Task.Run(() => Parallel.ForEach(cats, c => JunkService.Analyze(c)));
            _junkLoaded = true;
            UpdateJunkTotal();
        }
        finally { HideBusy(); }
    }

    private void UpdateJunkTotal() =>
        JunkTotalText.Text = L.F("JunkTotal", Fmt.Size(_junk.Where(c => c.IsSelected).Sum(c => c.Size)));

    private void JunkCheck_Click(object sender, RoutedEventArgs e) => UpdateJunkTotal();

    private async void JunkAnalyze_Click(object sender, RoutedEventArgs e) => await SafeAsync(AnalyzeJunkAsync);

    private async void JunkClean_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        if (_busy) return;
        var cats = _junk.Where(c => c.IsSelected).ToList();
        if (cats.Count == 0) return;

        ShowBusy(L.Get("Deleting"));
        long freed = 0;
        try { freed = await Task.Run(() => cats.Sum(c => JunkService.Clean(c))); }
        finally { HideBusy(); }

        Dialogs.Info(this, L.F("JunkDone", Fmt.Size(freed)));
        await AnalyzeJunkAsync();
    });

    // ===================== Settings =====================

    private void LoadSettingsUi()
    {
        var s = AppSettings.Current;
        LangAr.IsChecked = s.Language == "ar";
        LangEn.IsChecked = s.Language != "ar";
        OptRestore.IsChecked = s.CreateRestorePoint;
        OptBackup.IsChecked = s.BackupRegistry;
        _settingsReady = true;
    }

    private void Lang_Checked(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        var lang = LangAr.IsChecked == true ? "ar" : "en";
        if (lang == AppSettings.Current.Language) return;
        AppSettings.Current.Language = lang;
        AppSettings.Save();
        Dialogs.Info(this, L.Get("RestartNote") + "\n\n" + (lang == "ar" ? "يرجى إعادة التشغيل." : "Please restart."));
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        AppSettings.Current.CreateRestorePoint = OptRestore.IsChecked == true;
        AppSettings.Current.BackupRegistry = OptBackup.IsChecked == true;
        AppSettings.Save();
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e) => OpenDir(AppPaths.BackupDir);
    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenDir(AppPaths.LogDir);

    private static void OpenDir(string dir)
    {
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }
}
