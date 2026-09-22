using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Stats.App.Controls;
using Stats.App.Helpers;
using Stats.Core.Frames;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class DashboardWindow : Window
{
    private const string DragFormat = "Stats.TileId";
    private Point _dragStart;
    private string? _dragTileId;
    /// <summary>The single live drag-reorder insertion-line adorner (v1.8 §7a) — always removed before a new one
    /// is added, and on DragLeave/Drop/drag end, so it can never leak or duplicate across tiles.</summary>
    private InsertionAdorner? _insertionAdorner;

    // ---- Free/Snap canvas drag state (dashboard layout modes) — one "in-flight drag" slot each for a tile
    // container and the core-matrix block container; never more than one drag is in flight at a time (mouse
    // capture guarantees this), so a single set of fields per kind is enough.
    private FrameworkElement? _freeDragContainer;
    private Point _freeDragMouseStart;
    private Point _freeDragOrigin;
    private bool _freeDragExceededThreshold;
    private FrameworkElement? _coreDragContainer;
    private Point _coreDragMouseStart;
    private Point _coreDragOrigin;
    private bool _coreDragExceededThreshold;

    // ---- Free/Snap canvas resize state (tile-resize-by-drag) — one "in-flight resize" slot, mirroring the
    // move-drag fields above; mouse capture guarantees a resize and a move are never both in flight for the
    // same container. _resizeCandidate is the live nearest-preset (TileDimensions.Nearest), recomputed on every
    // PreviewMouseMove and applied only on commit.
    private FrameworkElement? _resizeContainer;
    private string? _resizeTileId;
    private Point _resizeMouseStart;
    private double _resizeOriginWidth;
    private double _resizeOriginHeight;
    private bool _resizeExceededThreshold;
    private TileSize _resizeCandidate;
    private ResizeOutlineAdorner? _resizeAdorner;
    /// <summary>The layer <see cref="_resizeAdorner"/> was added to — removal goes through this rather than re-looking
    /// it up from the adorned container, which may already be detached (a rebuild mid-drag) and would return null,
    /// leaving the outline painted forever.</summary>
    private AdornerLayer? _resizeAdornerLayer;

    /// <summary>Whichever header button (Metrics or Settings) most recently opened the flyout — Escape returns
    /// keyboard focus here (DESIGN.md §5); falls back to the Metrics button if the flyout was opened some other
    /// way (e.g. the empty-state "Open Metrics" button).</summary>
    private Button? _pickerOpener;
    private ListCollectionView? _pickerView;

    /// <summary>Set by App: true only when exiting via tray menu; otherwise close hides to tray.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Raw settings the tile context menu reads (unresolved TilePref kind, threshold rules/overrides).
    /// Set by the composition root; a non-production host (tools/Stats.UiPreview) sets it too. Falls back to the
    /// production <see cref="App.Settings"/> when unset so existing behavior is unchanged.</summary>
    public AppSettings? Settings { get; set; }

    private AppSettings? EffectiveSettings => Settings ?? (Application.Current as App)?.Settings;

    private DashboardViewModel? Vm => DataContext as DashboardViewModel;

    public DashboardWindow()
    {
        InitializeComponent();
        InitializeCinema();
        DarkTitleBar.Apply(this);
        DataContextChanged += (_, _) =>
        {
            if (Vm is not DashboardViewModel vm) return;
            var pickerView = new ListCollectionView(vm.PickerItems);
            pickerView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MetricPickerItem.GroupName)));
            pickerView.Filter = o => o is MetricPickerItem item && vm.PickerMatches(item);
            _pickerView = pickerView;
            PickerList.ItemsSource = pickerView;
            UpdatePickerNoResults();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DashboardViewModel.PickerFilter))
                {
                    pickerView.Refresh();
                    UpdatePickerNoResults();
                }
            };
        };
    }

    /// <summary>Toggles the Metrics tab's no-results panel from <see cref="ListCollectionView.IsEmpty"/> — computed
    /// after every filter change rather than bound, since the filtered-empty state depends on the live collection
    /// view, not a view-model property (DESIGN.md §5 "helpful no-results state").</summary>
    private void UpdatePickerNoResults()
    {
        if (_pickerView is null) return;
        PickerNoResults.Visibility = _pickerView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
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
        if (Vm?.IsLayoutLocked == true) return;
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
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, id), DragDropEffects.Move);
        }
        finally
        {
            // Safety net for a cancelled drag (e.g. Esc) that ends without a Drop or a final DragLeave reaching
            // the last-hovered tile — the adorner must never survive past the drag gesture.
            RemoveInsertionAdorner();
        }
    }

    private void Tile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragTileId = null;

    private void Tile_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement target || target.DataContext is not MetricTileViewModel targetTile
            || !e.Data.GetDataPresent(DragFormat) || e.Data.GetData(DragFormat) is not string fromId
            || Vm is not DashboardViewModel vm || vm.IsLayoutLocked || vm.GroupOf(fromId) != vm.GroupOf(targetTile.Definition.Id))
        {
            e.Effects = DragDropEffects.None;
            RemoveInsertionAdorner();
            return;
        }
        e.Effects = DragDropEffects.Move;
        ShowInsertionAdorner(target);
    }

    private void Tile_DragLeave(object sender, DragEventArgs e) => RemoveInsertionAdorner();

    private void Tile_Drop(object sender, DragEventArgs e)
    {
        RemoveInsertionAdorner();
        if (!e.Data.GetDataPresent(DragFormat)) return;
        if ((sender as FrameworkElement)?.DataContext is not MetricTileViewModel target) return;
        var fromId = e.Data.GetData(DragFormat) as string;
        if (fromId is null) return;
        Vm?.MoveTile(fromId, target.Definition.Id); // cross-group drops are ignored by the VM
        e.Handled = true;
    }

    /// <summary>Shows the single insertion-line adorner on <paramref name="target"/>, moving it there if it was
    /// already showing elsewhere. No-op (avoids an adorner-layer round trip) when it's already on this target.</summary>
    private void ShowInsertionAdorner(FrameworkElement target)
    {
        if (_insertionAdorner?.AdornedElement == target) return;
        RemoveInsertionAdorner();
        if (AdornerLayer.GetAdornerLayer(target) is not AdornerLayer layer) return;
        _insertionAdorner = new InsertionAdorner(target);
        layer.Add(_insertionAdorner);
    }

    private void RemoveInsertionAdorner()
    {
        if (_insertionAdorner is null) return;
        AdornerLayer.GetAdornerLayer(_insertionAdorner.AdornedElement)?.Remove(_insertionAdorner);
        _insertionAdorner = null;
    }

    // ---- Free/Snap canvas: drag + keyboard nudge (dashboard layout modes) ----
    //
    // Unlike Auto's DragDrop.DoDragDrop reorder above, a Free/Snap container is moved by writing Canvas.Left/Top
    // directly on PreviewMouseMove (SetCurrentValue, which updates the rendered position without breaking the
    // ItemContainerStyle's Canvas.Left/Top -> X/Y binding — the binding re-applies its own value the next time the
    // VM raises PropertyChanged, e.g. once SetTilePosition's clamp/snap lands), then committing once to the VM on
    // mouse-up (or on an unexpected LostMouseCapture, e.g. Esc — CommitFreeDrag/CommitCoreDrag are shared by both).
    // A move that never exceeds the system drag threshold is treated as a click: nothing is written back, so the
    // container simply re-shows its bound (unchanged) position.

    private void FreeTile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement container) return;
        if (FindAncestorButtonOrSelf(e.OriginalSource as DependencyObject, container) is not null) return; // never drag from "…" or another button
        if (container.DataContext is not MetricTileViewModel tile) return;
        if (Vm?.IsLayoutLocked == true) return;

        if (FindAncestorNamedOrSelf(e.OriginalSource as DependencyObject, "TileResizeGrip", container) is not null)
        {
            StartFreeResize(container, tile, e); // grip hit: always a resize, never a move, no double-click action
            return;
        }

        if (e.ClickCount == 2)
        {
            Vm?.OpenTileDetail(tile.Definition.Id);
            return;
        }
        container.Focus(); // makes arrow-key nudge reachable right after this drag/click, not just via Tab-cycling
        _freeDragContainer = container;
        _freeDragMouseStart = e.GetPosition(ParentCanvas(container));
        _freeDragOrigin = new Point(Canvas.GetLeft(container), Canvas.GetTop(container));
        _freeDragExceededThreshold = false;
        container.CaptureMouse();
    }

    private void FreeTile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeContainer is not null && ReferenceEquals(sender, _resizeContainer))
        {
            UpdateFreeResize(e);
            return;
        }
        if (_freeDragContainer is null || !ReferenceEquals(sender, _freeDragContainer) || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(ParentCanvas(_freeDragContainer)) - _freeDragMouseStart;
        if (!_freeDragExceededThreshold)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _freeDragExceededThreshold = true;
        }
        _freeDragContainer.SetCurrentValue(Canvas.LeftProperty, _freeDragOrigin.X + delta.X);
        _freeDragContainer.SetCurrentValue(Canvas.TopProperty, _freeDragOrigin.Y + delta.Y);
    }

    private void FreeTile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeContainer is not null) CommitFreeResize();
        else CommitFreeDrag();
    }

    private void FreeTile_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_resizeContainer is not null) CommitFreeResize();
        else CommitFreeDrag();
    }

    private void CommitFreeDrag()
    {
        if (_freeDragContainer is null) return;
        var container = _freeDragContainer;
        _freeDragContainer = null;
        container.ReleaseMouseCapture();
        if (!_freeDragExceededThreshold) return; // a click, not a drag — position unchanged
        if (PresentationSource.FromVisual(container) is null) return;
        if (container.DataContext is MetricTileViewModel tile)
        {
            Vm?.SetTilePosition(tile.Definition.Id, Canvas.GetLeft(container), Canvas.GetTop(container));
            PushFinalPosition(container, tile.X, tile.Y);
        }
    }

    // ---- Free/Snap canvas: resize grip drag (tile-resize-by-drag) ----
    //
    // A grip drag never moves the tile (position is anchored top-left — owner decision §8) and never opens the
    // detail window. While dragging, only a ResizeOutlineAdorner is updated (raw dragged rect + nearest S/M/L
    // preset rect + letter) — the real tile's Width/Height (driven by TilePref.Size via TileSizeToLength) is left
    // alone until CommitFreeResize calls DashboardViewModel.SetTileSize on release, exactly like the tile menu's
    // existing Size items. A drag below the system drag threshold is a click (no change), and Esc cancels
    // (CancelFreeResize) without applying anything.

    private void StartFreeResize(FrameworkElement container, MetricTileViewModel tile, MouseButtonEventArgs e)
    {
        container.Focus();
        _resizeContainer = container;
        _resizeTileId = tile.Definition.Id;
        _resizeMouseStart = e.GetPosition(ParentCanvas(container));
        _resizeOriginWidth = tile.Width;
        _resizeOriginHeight = tile.Height;
        _resizeExceededThreshold = false;
        _resizeCandidate = tile.Size;
        container.CaptureMouse();
        e.Handled = true;
    }

    private void UpdateFreeResize(MouseEventArgs e)
    {
        if (_resizeContainer is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(ParentCanvas(_resizeContainer)) - _resizeMouseStart;
        if (!_resizeExceededThreshold)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _resizeExceededThreshold = true;
            if (AdornerLayer.GetAdornerLayer(_resizeContainer) is AdornerLayer layer)
            {
                _resizeAdorner = new ResizeOutlineAdorner(_resizeContainer);
                _resizeAdornerLayer = layer;
                layer.Add(_resizeAdorner);
            }
        }
        double w = Math.Max(1, _resizeOriginWidth + delta.X);
        double h = Math.Max(1, _resizeOriginHeight + delta.Y);
        _resizeCandidate = TileDimensions.Nearest(w, h);
        var (candidateW, candidateH) = TileDimensions.Of(_resizeCandidate);
        _resizeAdorner?.Update(new Rect(0, 0, w, h), new Rect(0, 0, candidateW, candidateH), _resizeCandidate.ToString());
    }

    /// <summary>Esc while a resize is in flight (<see cref="FreeTile_PreviewKeyDown"/>): releases capture, removes
    /// the adorner, applies nothing — the tile's size is left exactly as it was.</summary>
    private void CancelFreeResize()
    {
        if (_resizeContainer is null) return;
        var container = _resizeContainer;
        _resizeContainer = null;
        _resizeTileId = null;
        RemoveResizeAdorner();
        container.ReleaseMouseCapture();
    }

    /// <summary>From <see cref="FreeTile_PreviewMouseLeftButtonUp"/> and <see cref="FreeTile_LostMouseCapture"/>,
    /// same nulling-before-release order as <see cref="CommitFreeDrag"/>: null the slot, remove the adorner,
    /// release capture, then — only if the drag threshold was exceeded and the candidate differs from the tile's
    /// current size — call <see cref="DashboardViewModel.SetTileSize"/> (after every field read off the container
    /// is done; <c>RebuildSections</c> destroys it) and restore focus to the new container for the same id.</summary>
    private void CommitFreeResize()
    {
        if (_resizeContainer is null) return;
        var container = _resizeContainer;
        var id = _resizeTileId!;
        var candidate = _resizeCandidate;
        var exceeded = _resizeExceededThreshold;
        _resizeContainer = null;
        _resizeTileId = null;
        RemoveResizeAdorner();
        container.ReleaseMouseCapture();
        if (!exceeded) return; // a click, not a drag — size unchanged
        if (container.DataContext is MetricTileViewModel tile && tile.Size != candidate && Vm is DashboardViewModel vm)
        {
            vm.SetTileSize(id, candidate);
            FocusTileById(id);
        }
    }

    private void RemoveResizeAdorner()
    {
        if (_resizeAdorner is null) return;
        _resizeAdornerLayer?.Remove(_resizeAdorner);
        _resizeAdorner = null;
        _resizeAdornerLayer = null;
    }

    /// <summary>The Canvas panel hosting <paramref name="container"/> — Free/Snap move and resize drags measure
    /// mouse positions against this (owner decision assumed §9), not the window, so deltas stay 1:1 under
    /// ScaledRoot's UI-scale LayoutTransform at every DashboardUiScale.</summary>
    private static Canvas ParentCanvas(FrameworkElement container) => (Canvas)VisualTreeHelper.GetParent(container);

    /// <summary>After a drag commits, deterministically re-paint Canvas.Left/Top with the VM's final (possibly
    /// clamped/snapped) value. <c>Canvas.Left</c>/<c>Top</c> come from a Style setter binding, not a local one — a
    /// <c>BindingOperations.GetBindingExpression(...).UpdateTarget()</c> call against a style-sourced expression is
    /// not a guaranteed retrieval, and when it returns null a Snap-mode drop that snaps back to the value the VM
    /// already held (no PropertyChanged) leaves the raw drag position painted on screen. Pushing the VM's value
    /// straight back with SetCurrentValue is deterministic either way, and doesn't fight the Style setter binding
    /// the way a local value assignment would.</summary>
    private static void PushFinalPosition(FrameworkElement container, double x, double y)
    {
        container.SetCurrentValue(Canvas.LeftProperty, x);
        container.SetCurrentValue(Canvas.TopProperty, y);
    }

    /// <summary>Arrow-key nudge, added alongside (not instead of) <see cref="Tile_PreviewKeyDown"/>'s reused
    /// Shift+F10/Apps context-menu handling — both are wired as separate EventSetters on the same PreviewKeyDown
    /// event in the Free canvas's ItemContainerStyle.</summary>
    private void FreeTile_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _resizeContainer is not null && ReferenceEquals(sender, _resizeContainer))
        {
            CancelFreeResize();
            e.Handled = true;
            return;
        }
        if (Vm is not DashboardViewModel vm) return;
        if ((sender as FrameworkElement)?.DataContext is not MetricTileViewModel tile) return;
        if (!TryGetArrowDelta(e.Key, Keyboard.Modifiers, out var dx, out var dy)) return;
        double step = DashboardLayout.GridSize * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 4 : 1);
        vm.SetTilePosition(tile.Definition.Id, tile.X + dx * step, tile.Y + dy * step);
        e.Handled = true;
    }

    private void CoreMatrixBlock_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement container) return;
        if (FindAncestorButtonOrSelf(e.OriginalSource as DependencyObject, container) is not null) return; // mirrors FreeTile_*: never drag from a button
        if (e.ClickCount == 2) return; // mirrors FreeTile_*'s double-click branch; the block has no double-click action, so just don't start a drag
        if (Vm?.IsLayoutLocked == true) return;
        container.Focus(); // makes arrow-key nudge reachable right after this drag/click, not just via Tab-cycling
        _coreDragContainer = container;
        _coreDragMouseStart = e.GetPosition(ParentCanvas(container));
        _coreDragOrigin = new Point(Canvas.GetLeft(container), Canvas.GetTop(container));
        _coreDragExceededThreshold = false;
        container.CaptureMouse();
    }

    private void CoreMatrixBlock_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_coreDragContainer is null || !ReferenceEquals(sender, _coreDragContainer) || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(ParentCanvas(_coreDragContainer)) - _coreDragMouseStart;
        if (!_coreDragExceededThreshold)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _coreDragExceededThreshold = true;
        }
        _coreDragContainer.SetCurrentValue(Canvas.LeftProperty, _coreDragOrigin.X + delta.X);
        _coreDragContainer.SetCurrentValue(Canvas.TopProperty, _coreDragOrigin.Y + delta.Y);
    }

    private void CoreMatrixBlock_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => CommitCoreDrag();
    private void CoreMatrixBlock_LostMouseCapture(object sender, MouseEventArgs e) => CommitCoreDrag();

    private void CommitCoreDrag()
    {
        if (_coreDragContainer is null) return;
        var container = _coreDragContainer;
        _coreDragContainer = null;
        container.ReleaseMouseCapture();
        if (!_coreDragExceededThreshold) return;
        if (PresentationSource.FromVisual(container) is null) return;
        if (Vm is DashboardViewModel vm)
        {
            vm.SetCoreMatrixPosition(Canvas.GetLeft(container), Canvas.GetTop(container));
            PushFinalPosition(container, vm.CoreMatrixX, vm.CoreMatrixY);
        }
    }

    private void CoreMatrixBlock_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not DashboardViewModel vm) return;
        if (!TryGetArrowDelta(e.Key, Keyboard.Modifiers, out var dx, out var dy)) return;
        double step = DashboardLayout.GridSize * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 4 : 1);
        vm.SetCoreMatrixPosition(vm.CoreMatrixX + dx * step, vm.CoreMatrixY + dy * step);
        e.Handled = true;
    }

    /// <summary>Reports the block's measured pixel size once it has been laid out, so the seed pack and the canvas
    /// extent (<see cref="DashboardViewModel"/>) can account for it — see <see cref="DashboardViewModel.SetCoreMatrixSize"/>.</summary>
    private void CoreMatrixBlock_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Vm?.SetCoreMatrixSize(e.NewSize.Width, e.NewSize.Height);

    private static bool TryGetArrowDelta(Key key, ModifierKeys modifiers, out double dx, out double dy)
    {
        dx = dy = 0;
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return false;
        switch (key)
        {
            case Key.Left: dx = -1; return true;
            case Key.Right: dx = 1; return true;
            case Key.Up: dy = -1; return true;
            case Key.Down: dy = 1; return true;
            default: return false;
        }
    }

    /// <summary>Walks up from <paramref name="source"/>, no further than <paramref name="boundary"/> (inclusive),
    /// looking for a Button — used to keep a click on the tile's hover "…" menu button (or any other interactive
    /// child a future template might add) from also starting a Free/Snap drag. Bounding the walk at the drag
    /// container (rather than walking all the way up into the window chrome) means an ancestor Button outside the
    /// container — e.g. the window's own header buttons — can never suppress dragging.</summary>
    private static Button? FindAncestorButtonOrSelf(DependencyObject? source, DependencyObject boundary)
    {
        while (source is not null)
        {
            if (source is Button b) return b;
            if (ReferenceEquals(source, boundary)) break;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    /// <summary>Sibling of <see cref="FindAncestorButtonOrSelf"/>: walks up from <paramref name="source"/>, no
    /// further than <paramref name="boundary"/> (inclusive), looking for a <see cref="FrameworkElement"/> named
    /// <paramref name="name"/> — used to detect a mouse-down starting on the resize grip (<c>"TileResizeGrip"</c>,
    /// the container's ControlTemplate) so it starts a resize instead of a move-drag.</summary>
    private static DependencyObject? FindAncestorNamedOrSelf(DependencyObject? source, string name, DependencyObject boundary)
    {
        while (source is not null)
        {
            if (source is FrameworkElement fe && fe.Name == name) return source;
            if (ReferenceEquals(source, boundary)) break;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    /// <summary>Restores keyboard focus to the container for tile <paramref name="id"/> after a size change
    /// (grip drag or Ctrl+Plus/Minus) rebuilds every tile view model and container from scratch
    /// (<see cref="DashboardViewModel.RebuildSections"/>) — deferred to <see cref="DispatcherPriority.Loaded"/>
    /// (same idiom as <see cref="PickerFlyout_IsVisibleChanged"/>) so the new containers actually exist by the
    /// time this runs. Searches the Free canvas directly, or, in Auto, each section's nested tiles
    /// <see cref="ItemsControl"/> — found via the outer Sections <see cref="ItemsControl"/>'s generated
    /// <see cref="ContentPresenter.ContentTemplate"/> (the default ItemsControl→ContentPresenter container
    /// generation copies <c>ItemTemplate</c> to <c>ContentTemplate</c>), then <c>FindName</c> within it.</summary>
    private void FocusTileById(string id)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Vm is not DashboardViewModel vm) return;
            if (vm.IsAutoLayout)
            {
                foreach (var section in vm.Sections)
                {
                    var tile = section.Tiles.FirstOrDefault(t => t.Definition.Id == id);
                    if (tile is null) continue;
                    if (SectionsList.ItemContainerGenerator.ContainerFromItem(section) is not ContentPresenter sectionContainer) return;
                    if (sectionContainer.ContentTemplate?.FindName("SectionTilesList", sectionContainer) is not ItemsControl nested) return;
                    if (nested.ItemContainerGenerator.ContainerFromItem(tile) is UIElement tileContainer) tileContainer.Focus();
                    return;
                }
            }
            else
            {
                var tile = vm.Tiles.FirstOrDefault(t => t.Definition.Id == id);
                if (tile is null) return;
                if (FreeTilesList.ItemContainerGenerator.ContainerFromItem(tile) is UIElement tileContainer) tileContainer.Focus();
            }
        }), DispatcherPriority.Loaded);
    }

    // ---- tile context menu ----

    private void Tile_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MetricTileViewModel tile) return;
        OpenTileMenu((FrameworkElement)sender, tile);
        e.Handled = true;
    }

    /// <summary>Handles the hover "⋯" button's Click bubbling up from inside a tile's DataTemplate (see the
    /// EventSetter on the tile ItemContainerStyle — TileTemplates.xaml has no code-behind of its own, so the
    /// button's routed Click is caught here instead). Only the named menu button is handled; any other future
    /// button inside a tile template would just bubble through untouched.</summary>
    private void TileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { Name: "TileMenuButton" } button) return;
        if (button.DataContext is not MetricTileViewModel tile) return;
        OpenTileMenu(button, tile);
        e.Handled = true;
    }

    /// <summary>Shift+F10 or the Menu/Apps key opens the same context menu while a tile container has keyboard
    /// focus (see the ItemContainerStyle's Focusable/IsTabStop/FocusVisualStyle setters).</summary>
    private void Tile_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryStepTileSize(sender, e)) { e.Handled = true; return; }

        bool isMenuKey = e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift);
        if (!isMenuKey) return;
        if ((sender as FrameworkElement)?.DataContext is not MetricTileViewModel tile) return;
        OpenTileMenu((FrameworkElement)sender, tile);
        e.Handled = true;
    }

    /// <summary>Ctrl+Plus/Minus (owner decision assumed §7): steps the tile's size S↔M↔L, saturating silently at
    /// either end. Wired through the shared <see cref="Tile_PreviewKeyDown"/> EventSetter — already present on
    /// both the Auto and Free/Snap tile ItemContainerStyles for Shift+F10/Apps — so it works in either layout
    /// without a second EventSetter. Reports "handled" for the whole key combination once it matches (even at
    /// saturation, where <see cref="DashboardViewModel.StepTileSize"/> is a no-op) so the ScrollViewer never sees
    /// Ctrl+Plus/Minus as a scroll gesture.</summary>
    private bool TryStepTileSize(object sender, KeyEventArgs e)
    {
        // A grip drag is in flight: stepping now would rebuild the container under the captured mouse and make
        // LostMouseCapture apply the drag candidate on top — ignore the key until the drag ends (Esc cancels it).
        if (_resizeContainer is not null) return false;
        // Ctrl is required; Shift is tolerated because "+" is Shift+OemPlus on a US layout (the browser/VS zoom
        // convention, and what the menu's "Ctrl++" gesture text promises); Alt/Win never match.
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) == 0 || (mods & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return false;
        int delta = e.Key switch
        {
            Key.OemPlus or Key.Add => 1,
            Key.OemMinus or Key.Subtract => -1,
            _ => 0,
        };
        if (delta == 0) return false;
        if (Vm is DashboardViewModel vm && (sender as FrameworkElement)?.DataContext is MetricTileViewModel tile)
        {
            vm.StepTileSize(tile.Definition.Id, delta);
            FocusTileById(tile.Definition.Id);
        }
        return true;
    }
    /// <summary>Every kind offered in the "Tile kind" submenu, in menu order. "Histogram" is offered on every
    /// tile; "FPS summary" is gated to <see cref="GameMetricRole.Fps"/> tiles below (owner decision B).</summary>
    private static readonly TileKind[] MenuKinds =
    {
        TileKind.Auto, TileKind.Sparkline, TileKind.Gauge, TileKind.Bar, TileKind.Value, TileKind.Histogram, TileKind.FpsSummary,
    };

    /// <summary>Menu label for a kind — every existing kind keeps its identical ToString() header ("Auto",
    /// "Sparkline", "Gauge", "Bar", "Value"; "Histogram" also reads fine as-is); only FpsSummary gets a friendlier
    /// two-word label.</summary>
    private static string KindHeader(TileKind k) => k switch { TileKind.FpsSummary => "FPS summary", _ => k.ToString() };

    /// <summary>Single builder for the tile context menu — used by right-click, the hover "⋯" button, and
    /// Shift+F10/Apps so all three entry points stay in lockstep (see v1.8 §7b).</summary>
    private void OpenTileMenu(FrameworkElement target, MetricTileViewModel tile)
    {
        if (Vm is not DashboardViewModel vm) return;
        var id = tile.Definition.Id;

        var menu = new ContextMenu { PlacementTarget = target };

        var kind = new MenuItem { Header = "Tile kind", IsEnabled = !vm.IsLayoutLocked };
        foreach (var k in MenuKinds)
        {
            if (k == TileKind.FpsSummary && tile.GameRole != GameMetricRole.Fps) continue;
            var mi = new MenuItem { Header = KindHeader(k), IsCheckable = true, IsChecked = CurrentPrefKind(id) == k };
            mi.Click += (_, _) => vm.SetTileKindEditCommand.Execute(new TileKindEdit(id, k));
            kind.Items.Add(mi);
        }
        var size = new MenuItem { Header = "Size", IsEnabled = !vm.IsLayoutLocked };
        foreach (var s in new[] { TileSize.S, TileSize.M, TileSize.L })
        {
            var mi = new MenuItem { Header = s switch { TileSize.S => "Small", TileSize.L => "Large", _ => "Medium" }, IsCheckable = true, IsChecked = tile.Size == s };
            mi.Click += (_, _) =>
            {
                vm.SetTileSizeEditCommand.Execute(new TileSizeEdit(id, s));
                FocusTileById(id);
            };
            size.Items.Add(mi);
        }
        size.Items.Add(new Separator());
        var larger = new MenuItem { Header = "Larger", InputGestureText = "Ctrl++", IsEnabled = tile.Size != TileSize.L };
        larger.Click += (_, _) =>
        {
            vm.StepTileSizeEditCommand.Execute(new TileSizeStep(id, 1));
            FocusTileById(id);
        };
        var smaller = new MenuItem { Header = "Smaller", InputGestureText = "Ctrl+-", IsEnabled = tile.Size != TileSize.S };
        smaller.Click += (_, _) =>
        {
            vm.StepTileSizeEditCommand.Execute(new TileSizeStep(id, -1));
            FocusTileById(id);
        };
        size.Items.Add(larger);
        size.Items.Add(smaller);
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => PromptRename(vm, tile);
        var setMax = new MenuItem { Header = "Set gauge/bar max…" };
        setMax.Click += (_, _) => PromptMax(vm, tile);
        var thresholds = new MenuItem { Header = "Set warn/crit thresholds…" };
        thresholds.Click += (_, _) => PromptThresholds(vm, tile);
        var details = new MenuItem { Header = "Details…" };
        details.Click += (_, _) => vm.OpenTileDetail(id);
        var remove = new MenuItem { Header = "Remove from dashboard", IsEnabled = !vm.IsLayoutLocked };
        remove.Click += (_, _) => vm.RemoveTileCommand.Execute(id);

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
    }

    /// <summary>The raw (unresolved) pref kind — the tile's Kind property is the *resolved* kind.</summary>
    private TileKind CurrentPrefKind(string id) =>
        EffectiveSettings?.TilePrefs.TryGetValue(id, out var p) == true ? p.Kind : TileKind.Auto;

    private void PromptRename(DashboardViewModel vm, MetricTileViewModel tile)
    {
        var result = InputDialog.Show(this, "Rename tile", "Display name (blank = sensor name):", tile.DisplayName);
        if (result is not null) vm.RenameTileEditCommand.Execute(new TileRenameEdit(tile.Definition.Id, result));
    }

    private void PromptMax(DashboardViewModel vm, MetricTileViewModel tile)
    {
        var current = tile.Max is float m ? m.ToString("0.##", CultureInfo.InvariantCulture) : "";
        var result = InputDialog.Show(this, "Gauge / bar max", $"Max value in {tile.Unit} (blank = automatic):", current);
        if (result is null) return;
        if (result.Trim().Length == 0) { vm.SetTileMaxEditCommand.Execute(new TileMaxEdit(tile.Definition.Id, null)); return; }
        if (float.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) vm.SetTileMaxEditCommand.Execute(new TileMaxEdit(tile.Definition.Id, v));
    }

    private void PromptThresholds(DashboardViewModel vm, MetricTileViewModel tile)
    {
        var settings = EffectiveSettings;
        if (settings is null) return;
        var def = tile.Definition;
        var groupRule = settings.ThresholdRules.FirstOrDefault(r => r.Group == def.Group && r.Unit == def.Unit);
        settings.ThresholdOverrides.TryGetValue(def.Id, out var existing);
        if (!ThresholdDialog.Show(this, tile.DisplayName, def.Unit, groupRule, existing, out var result, out var cleared))
            return; // cancelled — no change
        vm.SetTileThresholdEditCommand.Execute(new TileThresholdEdit(def.Id, cleared ? null : result));
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

    // ---- header "View" menu (Collapse all / Expand all) ----

    /// <summary>Same code-behind-built-menu pattern as <see cref="OpenTileMenu"/> — a ContextMenu placed under
    /// the button rather than bound Command items, so it works regardless of ContextMenu's DataContext
    /// inheritance quirks. Also hosts the dashboard layout modes (Auto/Free/Snap) picker and "Reset tile
    /// positions…" (design's "View (DashboardWindow)" section).</summary>
    private void ViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not DashboardViewModel vm || sender is not FrameworkElement target) return;
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };

        menu.Items.Add(new MenuItem { Header = "Compare metrics…", Command = vm.OpenComparisonCommand });
        menu.Items.Add(new MenuItem { Header = "Gaming…", Command = vm.OpenGamingCommand });
        menu.Items.Add(new MenuItem { Header = "Beta lab…", Command = vm.OpenLabCommand });
        menu.Items.Add(new MenuItem { Header = "Theme studio…", Command = vm.OpenThemeStudioCommand });
        menu.Items.Add(new MenuItem { Header = "Scene builder / overlays…", Command = vm.OpenScenesCommand });
        var cinema = new MenuItem { Header = "Cinema mode (F11, Esc to leave)", IsCheckable = true, IsChecked = IsCinemaMode };
        cinema.Click += (_, _) => ToggleCinema();
        menu.Items.Add(cinema);
        menu.Items.Add(new MenuItem { Header = "Recordings…", Command = vm.OpenSessionsCommand });
        menu.Items.Add(new Separator());

        var expand = new MenuItem { Header = "Expand all", IsEnabled = vm.IsAutoLayout };
        expand.Click += (_, _) => vm.ExpandAllCommand.Execute(null);
        var collapse = new MenuItem { Header = "Collapse all", IsEnabled = vm.IsAutoLayout };
        collapse.Click += (_, _) => vm.CollapseAllCommand.Execute(null);
        menu.Items.Add(expand);
        menu.Items.Add(collapse);

        menu.Items.Add(new Separator());
        menu.Items.Add(LayoutModeItem("Auto arrange", DashboardLayoutMode.Auto, vm));
        menu.Items.Add(LayoutModeItem("Free", DashboardLayoutMode.Free, vm));
        menu.Items.Add(LayoutModeItem("Snap to grid", DashboardLayoutMode.Grid, vm));

        var reset = new MenuItem { Header = "Reset tile positions…", IsEnabled = !vm.IsAutoLayout };
        reset.Click += (_, _) => vm.ResetPositionsCommand.Execute(null);
        menu.Items.Add(new Separator());
        reset.IsEnabled &= !vm.IsLayoutLocked;
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        var locked = new MenuItem { Header = "Lock layout", IsCheckable = true, IsChecked = vm.IsLayoutLocked };
        locked.Click += (_, _) => vm.ToggleLayoutLockCommand.Execute(null);
        menu.Items.Add(locked);
        menu.Items.Add(new MenuItem { Header = "Undo layout edit", IsEnabled = vm.CanUndoLayoutEdit, Command = vm.UndoLayoutEditCommand });
        menu.Items.Add(BuildLayoutProfilesMenu(vm));

        menu.IsOpen = true;
    }

    private static MenuItem LayoutModeItem(string header, DashboardLayoutMode mode, DashboardViewModel vm)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = vm.LayoutMode == mode, IsEnabled = !vm.IsLayoutLocked };
        item.Click += (_, _) => vm.SetLayoutModeCommand.Execute(mode);
        return item;
    }

    private MenuItem BuildLayoutProfilesMenu(DashboardViewModel vm)
    {
        var active = vm.ActiveLayoutProfileName;
        var header = active is null ? "Layout profiles" : vm.IsLayoutModified ? $"Layout profile: {active} (modified)" : $"Layout profile: {active}";
        var menu = new MenuItem { Header = header };
        if (vm.LayoutProfileNames.Count == 0) menu.Items.Add(new MenuItem { Header = "No saved layouts yet", IsEnabled = false });
        foreach (var name in vm.LayoutProfileNames)
            menu.Items.Add(new MenuItem { Header = name, IsCheckable = true, IsChecked = name == active, Command = vm.LoadLayoutProfileCommand, CommandParameter = name });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Save", Command = vm.SaveActiveLayoutProfileCommand });
        menu.Items.Add(new MenuItem { Header = "Revert", Command = vm.RevertLayoutProfileCommand });
        var saveAs = new MenuItem { Header = "Save as…" };
        saveAs.Click += (_, _) => { var name = InputDialog.Show(this, "Save layout profile", "Layout name:", active ?? ""); if (!string.IsNullOrWhiteSpace(name)) vm.SaveLayoutProfileCommand.Execute(name.Trim()); };
        menu.Items.Add(saveAs);
        var rename = new MenuItem { Header = "Rename…", IsEnabled = active is not null };
        rename.Click += (_, _) => { var name = InputDialog.Show(this, "Rename layout profile", "New name:", active!); if (!string.IsNullOrWhiteSpace(name)) vm.RenameLayoutProfile(active!, name); };
        menu.Items.Add(rename);
        menu.Items.Add(new MenuItem { Header = active is null ? "Delete" : $"Delete \"{active}\"", Command = vm.DeleteLayoutProfileCommand, CommandParameter = active });
        return menu;
    }

    // ---- flyout close / Escape / focus ----

    /// <summary>Remembers whichever header button opened the flyout so Escape can return focus there.</summary>
    private void HeaderOpener_Click(object sender, RoutedEventArgs e) => _pickerOpener = sender as Button;

    private void PickerClose_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is DashboardViewModel vm) vm.IsPickerOpen = false;
        (_pickerOpener ?? MetricsButton).Focus();
    }

    /// <summary>Bubbling handler on the flyout Border (DESIGN.md §5): an open ComboBox dropdown or other child
    /// popup/editor consumes Escape itself (marks it Handled) before it ever reaches here, so this only fires for
    /// an Escape the flyout itself should act on.</summary>
    private void PickerFlyout_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Vm is not DashboardViewModel vm || !vm.IsPickerOpen) return;
        e.Handled = true;
        vm.IsPickerOpen = false;
        (_pickerOpener ?? MetricsButton).Focus();
    }

    /// <summary>Moves keyboard focus into the flyout as soon as it becomes visible: the search box for Metrics,
    /// the active tab header for Settings (DESIGN.md §5). Deferred one dispatcher pass so the newly-visible
    /// content is actually there to focus.</summary>
    private void PickerFlyout_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || Vm is not DashboardViewModel vm) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (vm.FlyoutTabIndex == 0) PickerFilterBox.Focus();
            else if (FlyoutTabs.ItemContainerGenerator.ContainerFromIndex(vm.FlyoutTabIndex) is TabItem tab) tab.Focus();
        }), DispatcherPriority.Loaded);
    }

    // ---- picker filter clear ----

    private void PickerFilterClear_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is DashboardViewModel vm) vm.PickerFilter = "";
    }
}
