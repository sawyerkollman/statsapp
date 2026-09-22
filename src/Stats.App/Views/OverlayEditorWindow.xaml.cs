using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Stats.App.Helpers;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class OverlayEditorWindow : Window
{
    private readonly Dictionary<Thumb, (OverlayCanvasSlot Slot, double X, double Y, double Width, double Height)> _drags = new();
    public OverlayEditorWindow() { InitializeComponent(); DarkTitleBar.Apply(this); }
    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm && sender is Thumb thumb && _drags.TryGetValue(thumb, out var drag)) { drag.X += e.HorizontalChange; drag.Y += e.VerticalChange; _drags[thumb] = drag; vm.SelectedSlot = drag.Slot; vm.Move(drag.Slot, drag.X, drag.Y); } }
    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm && sender is Thumb thumb && _drags.TryGetValue(thumb, out var drag)) { drag.Width += e.HorizontalChange; drag.Height += e.VerticalChange; _drags[thumb] = drag; vm.SelectedSlot = drag.Slot; vm.Resize(drag.Slot, drag.Width, drag.Height); } }
    private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
    { if (sender is Thumb thumb && thumb.DataContext is OverlayCanvasSlot slot) { _drags[thumb] = (slot, slot.X, slot.Y, slot.Width, slot.Height); if (DataContext is OverlayEditorViewModel vm) vm.SelectedSlot = slot; } }
    private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e) { if (sender is Thumb thumb) _drags.Remove(thumb); }
    private void Thumb_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (DataContext is OverlayEditorViewModel vm && sender is Thumb { DataContext: OverlayCanvasSlot slot }) vm.SelectedSlot = slot; }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    { if (DataContext is not OverlayEditorViewModel vm || !TryGetArrowNudge(Keyboard.FocusedElement, e.Key, out var slot, out var dx, out var dy)) return; vm.SelectedSlot = slot; vm.Nudge(slot, dx, dy); e.Handled = true; }
    private static bool TryGetArrowNudge(IInputElement? focusedElement, Key key, out OverlayCanvasSlot slot, out double dx, out double dy)
    {
        slot = (focusedElement as Thumb)?.DataContext as OverlayCanvasSlot ?? null!;
        dx = dy = 0;
        if (slot is null) return false;
        switch (key) { case Key.Left: dx = -8; break; case Key.Right: dx = 8; break; case Key.Up: dy = -8; break; case Key.Down: dy = 8; break; default: return false; }
        return true;
    }
}
