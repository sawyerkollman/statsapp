using System.Windows;
using System.Windows.Input;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class OverlayWindow : Window
{
    /// <summary>Esc was pressed while in move mode. The composition root owns exiting (click-through restore,
    /// tray menu header) — this window only reports the key press.</summary>
    public event Action? ExitMoveModeRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not OverlayViewModel { IsMoveMode: true }) return;
            e.Handled = true;
            ExitMoveModeRequested?.Invoke();
        };
    }
}
