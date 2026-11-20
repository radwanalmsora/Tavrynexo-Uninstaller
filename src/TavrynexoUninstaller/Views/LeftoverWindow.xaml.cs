using System.Collections.ObjectModel;
using System.Windows;
using Tavrynexo.Core;
using Tavrynexo.Models;
using Tavrynexo.Services;

namespace Tavrynexo.Views;

public partial class LeftoverWindow : Window
{
    private readonly ObservableCollection<LeftoverItem> _items;
    private readonly string _label;
    private bool _working;

    public LeftoverWindow(IEnumerable<LeftoverItem> items, string label)
    {
        InitializeComponent();
        FlowDirection = L.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        _label = label;
        _items = new ObservableCollection<LeftoverItem>(items);
        ItemList.ItemsSource = _items;
        SubText.Text = L.F("LeftoverSub", _items.Count);

        foreach (var i in _items) i.PropertyChanged += (_, _) => UpdateSelection();
        UpdateSelection();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.UseDarkTitleBar(this);
    }

    private void UpdateSelection()
    {
        var sel = _items.Where(i => i.IsSelected).ToList();
        SelText.Text = L.F("SelectedCount", sel.Count, Fmt.Size(sel.Sum(i => i.Size)));
        DeleteBtn.IsEnabled = sel.Count > 0 && !_working;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsSelected = true; }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsSelected = false; }
    private void Close_Click(object sender, RoutedEventArgs e) { if (!_working) Close(); }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_working) e.Cancel = true;
        base.OnClosing(e);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sel = _items.Where(i => i.IsSelected).ToList();
        if (sel.Count == 0 || _working) return;
        if (!Dialogs.Confirm(this, L.F("ConfirmDeleteLeftovers", sel.Count), true)) return;

        _working = true;
        BusyOverlay.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<string>(s => BusyText.Text = s);
            var backup = AppSettings.Current.BackupRegistry;
            var res = await Task.Run(() => CleanupService.Delete(sel, _label, backup, progress));

            var msg = L.F("DeleteSummary", res.Deleted, res.Failed, res.ScheduledForReboot,
                backup ? res.BackupFolder : "-");
            if (res.FailedItems.Count > 0)
                msg += L.F("FailedList", string.Join("\n", res.FailedItems.Take(10)));

            BusyOverlay.Visibility = Visibility.Collapsed;
            _working = false;
            Dialogs.Info(this, msg);
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("Delete leftovers", ex);
            BusyOverlay.Visibility = Visibility.Collapsed;
            _working = false;
            Dialogs.Error(this, ex.Message);
        }
    }
}
