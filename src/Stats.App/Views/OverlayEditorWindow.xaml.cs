using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Stats.App.Helpers;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class OverlayEditorWindow : Window
{
    private readonly Dictionary<Thumb, (OverlayCanvasSlot Slot, double X, double Y, double Width, double Height)> _drags = new();
    public OverlayEditorWindow() { InitializeComponent(); DarkTitleBar.Apply(this); }
    private void CanvasHost_SizeChanged(object sender, SizeChangedEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm) vm.FitToViewport(Math.Max(1, e.NewSize.Width - 20), Math.Max(1, e.NewSize.Height - 20)); }
    private void Fit_Click(object sender, RoutedEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm) vm.FitToViewport(Math.Max(1, CanvasHost.ViewportWidth - 20), Math.Max(1, CanvasHost.ViewportHeight - 20)); }
    private void Cards_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm && e.AddedItems.OfType<OverlayCanvasSlot>().LastOrDefault() is { } slot) vm.SetActive(slot); }
    private void Numeric_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm) vm.BeginNumericEdit(); }
    private void Numeric_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm) vm.CompleteNumericEdit(); }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OverlayEditorViewModel vm) return;
        var dialog = new SaveFileDialog { Filter = "Overlay canvas (*.json)|*.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, vm.ExportDraft()); vm.Error = "Canvas draft exported."; }
        catch (Exception ex) { vm.Error = "Could not export canvas: " + ex.Message; }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OverlayEditorViewModel vm) return;
        var dialog = new OpenFileDialog { Filter = "Overlay canvas (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        vm.ImportFile(dialog.FileName);
    }
    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm && sender is Thumb thumb && _drags.TryGetValue(thumb, out var drag)) { drag.X += e.HorizontalChange; drag.Y += e.VerticalChange; _drags[thumb] = drag; vm.Move(drag.Slot, drag.X, drag.Y); } }
    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm && sender is Thumb thumb && _drags.TryGetValue(thumb, out var drag)) { drag.Width += e.HorizontalChange; drag.Height += e.VerticalChange; _drags[thumb] = drag; vm.SelectedSlot = drag.Slot; vm.Resize(drag.Slot, drag.Width, drag.Height); } }
    private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
    { if (sender is Thumb thumb && thumb.DataContext is OverlayCanvasSlot slot) { _drags[thumb] = (slot, slot.X, slot.Y, slot.Width, slot.Height); if (DataContext is OverlayEditorViewModel vm) { vm.SelectForManipulation(slot, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); vm.BeginDrag(); } } }
    private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e) { if (sender is Thumb thumb) _drags.Remove(thumb); if (DataContext is OverlayEditorViewModel vm) vm.CompleteDrag(); }
    private void Thumb_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm && sender is Thumb { DataContext: OverlayCanvasSlot slot }) vm.SelectForManipulation(slot, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not OverlayEditorViewModel vm || Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Z) { vm.UndoCommand.Execute(null); e.Handled = true; return; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Y) { vm.RedoCommand.Execute(null); e.Handled = true; return; }
        if (!TryGetArrowNudge(Keyboard.FocusedElement, e.Key, out var slot, out var dx, out var dy)) return;
        vm.SelectForManipulation(slot, false); vm.Nudge(slot, dx, dy); e.Handled = true;
    }
    private static bool TryGetArrowNudge(IInputElement? focusedElement, Key key, out OverlayCanvasSlot slot, out double dx, out double dy)
    {
        slot = (focusedElement as Thumb)?.DataContext as OverlayCanvasSlot ?? null!;
        dx = dy = 0;
        if (slot is null) return false;
        switch (key) { case Key.Left: dx = -8; break; case Key.Right: dx = 8; break; case Key.Up: dy = -8; break; case Key.Down: dy = 8; break; default: return false; }
        return true;
    }
}
