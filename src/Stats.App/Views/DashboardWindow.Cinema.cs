using System.Windows;
using System.Windows.Input;

namespace Stats.App.Views;

public partial class DashboardWindow
{
    private Rect _cinemaBounds;
    private WindowState _cinemaState;
    private WindowStyle _cinemaStyle;
    private ResizeMode _cinemaResize;
    public bool IsCinemaMode { get; private set; }
    private void InitializeCinema()
    {
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11 || (e.Key == Key.Escape && IsCinemaMode))
            { ToggleCinema(); e.Handled = true; }
        };
        IsVisibleChanged += (_, e) => { if (e.NewValue is false && IsCinemaMode) ToggleCinema(); };
    }
    public void ToggleCinema()
    {
        if (!IsCinemaMode)
        {
            _cinemaBounds = RestoreBounds;
            _cinemaState = WindowState; _cinemaStyle = WindowStyle; _cinemaResize = ResizeMode;
            IsCinemaMode = true; // bounds callbacks must not persist intermediate fullscreen geometry
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal; WindowStyle = _cinemaStyle; ResizeMode = _cinemaResize;
            if (!_cinemaBounds.IsEmpty)
            { Left = _cinemaBounds.Left; Top = _cinemaBounds.Top; Width = _cinemaBounds.Width; Height = _cinemaBounds.Height; }
            WindowState = _cinemaState; IsCinemaMode = false;
        }
    }
}
