using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Stats.App.Helpers;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class DashboardWindow : Window
{
    private const string DragFormat = "Stats.TileId";
    private Point _dragStart;
    private string? _dragTileId;

    /// <summary>Set by App: true only when exiting via tray menu; otherwise close hides to tray.</summary>
    public bool AllowClose { get; set; }

    private DashboardViewModel? Vm => DataContext as DashboardViewModel;

    public DashboardWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        DataContextChanged += (_, _) =>
        {
            if (Vm is not DashboardViewModel vm) return;
            var pickerView = new ListCollectionView(vm.PickerItems);
            pickerView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MetricPickerItem.GroupName)));
            pickerView.Filter = o => o is MetricPickerItem item && vm.PickerMatches(item);
            PickerList.ItemsSource = pickerView;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DashboardViewModel.PickerFilter)) pickerView.Refresh();
            };
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    // ---- drag-reorder ----

    private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if ((sender as FrameworkElement)?.DataContext is MetricTileViewModel t) Vm?.OpenTileDetail(t.Definition.Id);
            _dragTileId = null;
            return;
        }
        _dragStart = e.GetPosition(this);
        _dragTileId = (sender as FrameworkElement)?.DataContext is MetricTileViewModel tile ? tile.Definition.Id : null;
    }

    private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTileId is null || e.LeftButton != MouseButtonState.Pressed) return;
        if ((sender as FrameworkElement)?.DataContext is not MetricTileViewModel t || t.Definition.Id != _dragTileId) return;
        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var id = _dragTileId;
        _dragTileId = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, id), DragDropEffects.Move);
    }

    private void Tile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragTileId = null;

    private void Tile_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Tile_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat)) return;
        if ((sender as FrameworkElement)?.DataContext is not MetricTileViewModel target) return;
        var fromId = e.Data.GetData(DragFormat) as string;
        if (fromId is null) return;
        Vm?.MoveTile(fromId, target.Definition.Id); // cross-group drops are ignored by the VM
        e.Handled = true;
    }

    // ---- tile context menu ----

    private void Tile_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not DashboardViewModel vm) return;
        if ((sender as FrameworkElement)?.DataContext is not MetricTileViewModel tile) return;
        var id = tile.Definition.Id;

        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };

        var kind = new MenuItem { Header = "Tile kind" };
        foreach (var k in new[] { TileKind.Auto, TileKind.Sparkline, TileKind.Gauge, TileKind.Bar, TileKind.Value })
        {
            var mi = new MenuItem { Header = k.ToString(), IsCheckable = true, IsChecked = CurrentPrefKind(id) == k };
            mi.Click += (_, _) => vm.SetTileKind(id, k);
            kind.Items.Add(mi);
        }
        var size = new MenuItem { Header = "Size" };
        foreach (var s in new[] { TileSize.S, TileSize.M, TileSize.L })
        {
            var mi = new MenuItem { Header = s switch { TileSize.S => "Small", TileSize.L => "Large", _ => "Medium" }, IsCheckable = true, IsChecked = tile.Size == s };
            mi.Click += (_, _) => vm.SetTileSize(id, s);
            size.Items.Add(mi);
        }
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => PromptRename(vm, tile);
        var setMax = new MenuItem { Header = "Set gauge/bar max…" };
        setMax.Click += (_, _) => PromptMax(vm, tile);
        var thresholds = new MenuItem { Header = "Set warn/crit thresholds…" };
        thresholds.Click += (_, _) => PromptThresholds(vm, tile);
        var details = new MenuItem { Header = "Details…" };
        details.Click += (_, _) => vm.OpenTileDetail(id);
        var remove = new MenuItem { Header = "Remove from dashboard" };
        remove.Click += (_, _) => vm.RemoveTile(id);

        menu.Items.Add(kind);
        menu.Items.Add(size);
        menu.Items.Add(new Separator());
        menu.Items.Add(rename);
        menu.Items.Add(setMax);
        menu.Items.Add(thresholds);
        menu.Items.Add(details);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>The raw (unresolved) pref kind — the tile's Kind property is the *resolved* kind.</summary>
    private static TileKind CurrentPrefKind(string id) =>
        (Application.Current as App)?.Settings?.TilePrefs.TryGetValue(id, out var p) == true ? p.Kind : TileKind.Auto;

    private void PromptRename(DashboardViewModel vm, MetricTileViewModel tile)
    {
        var result = InputDialog.Show(this, "Rename tile", "Display name (blank = sensor name):", tile.DisplayName);
        if (result is not null) vm.RenameTile(tile.Definition.Id, result);
    }

    private void PromptMax(DashboardViewModel vm, MetricTileViewModel tile)
    {
        var current = tile.Max is float m ? m.ToString("0.##", CultureInfo.InvariantCulture) : "";
        var result = InputDialog.Show(this, "Gauge / bar max", $"Max value in {tile.Unit} (blank = automatic):", current);
        if (result is null) return;
        if (result.Trim().Length == 0) { vm.SetTileMax(tile.Definition.Id, null); return; }
        if (float.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) vm.SetTileMax(tile.Definition.Id, v);
    }

    private void PromptThresholds(DashboardViewModel vm, MetricTileViewModel tile)
    {
        var settings = (Application.Current as App)?.Settings;
        if (settings is null) return;
        var def = tile.Definition;
        var groupRule = settings.ThresholdRules.FirstOrDefault(r => r.Group == def.Group && r.Unit == def.Unit);
        settings.ThresholdOverrides.TryGetValue(def.Id, out var existing);
        if (!ThresholdDialog.Show(this, tile.DisplayName, def.Unit, groupRule, existing, out var result, out var cleared))
            return; // cancelled — no change
        vm.SetTileThresholds(def.Id, cleared ? null : result);
    }

    // ---- picker group bulk ----

    private void PickerGroupAll_Click(object sender, RoutedEventArgs e) => BulkSelect(sender, true);
    private void PickerGroupNone_Click(object sender, RoutedEventArgs e) => BulkSelect(sender, false);

    private void BulkSelect(object sender, bool selected)
    {
        if ((sender as FrameworkElement)?.Tag is string groupName) Vm?.SelectAllInGroup(groupName, selected);
    }

    // ---- hotkey capture ----

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab) return;                       // let focus move on
        if (e.Key == Key.Escape) { e.Handled = true; Keyboard.ClearFocus(); return; }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return; // wait for the real key

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        string? name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.Space => "Space", Key.Pause => "Pause", Key.Insert => "Insert", Key.Delete => "Delete",
            Key.Home => "Home", Key.End => "End", Key.PageUp => "PageUp", Key.PageDown => "PageDown",
            Key.Scroll => "ScrollLock", Key.NumLock => "NumLock",
            _ => null,
        };
        if (name is null) return;
        parts.Add(name);
        if (Vm?.SettingsPanel is SettingsViewModel svm) svm.OverlayHotkey = string.Join("+", parts);
    }

    private void HotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SettingsPanel is SettingsViewModel svm) svm.OverlayHotkey = "";
    }
}
