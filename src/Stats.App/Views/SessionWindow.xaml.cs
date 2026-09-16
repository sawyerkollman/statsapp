using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Stats.App.Helpers;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class SessionWindow : Window
{
    private readonly DispatcherTimer _replayTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _loadingReplayWindow;
    public bool AllowClose { get; set; }
    public SessionWindow()
    {
        InitializeComponent(); DarkTitleBar.Apply(this);
        _replayTimer.Tick += async (_, _) =>
        {
            if (_loadingReplayWindow) return;
            if (DataContext is not SessionViewModel vm || !vm.CanOpenOrExport) { StopReplay(); return; }
            if (vm.ReplayIndex < vm.ReplayMaximum) vm.NextSampleCommand.Execute(null);
            else if (vm.HasNextWindow)
            {
                _loadingReplayWindow = true;
                try { await vm.NextWindowCommand.ExecuteAsync(null); }
                finally { _loadingReplayWindow = false; }
            }
            else StopReplay();
        };
        IsVisibleChanged += async (_, _) =>
        {
            if (!IsVisible) StopReplay();
            else if (DataContext is SessionViewModel vm) await vm.RefreshLibraryAsync();
        };
        Closed += (_, _) => StopReplay();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        StopReplay();
        if (!AllowClose) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SessionViewModel vm) return;
        var dialog = new OpenFileDialog { Filter = "Stats sessions (*.stats-session.jsonl)|*.stats-session.jsonl", InitialDirectory = vm.RecordingDirectory };
        if (dialog.ShowDialog(this) == true) { StopReplay(); await vm.OpenAsync(dialog.FileName); }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SessionViewModel vm) return;
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", DefaultExt = ".csv", InitialDirectory = vm.RecordingDirectory };
        if (dialog.ShowDialog(this) == true) { StopReplay(); await vm.ExportAsync(dialog.FileName); }
    }
    private async void OpenComparison_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SessionViewModel vm) return;
        var dialog = new OpenFileDialog { Filter = "Stats sessions (*.stats-session.jsonl)|*.stats-session.jsonl", InitialDirectory = vm.RecordingDirectory };
        if (dialog.ShowDialog(this) == true) { StopReplay(); await vm.OpenComparisonAsync(dialog.FileName); }
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_replayTimer.IsEnabled) StopReplay();
        else if (DataContext is SessionViewModel vm && vm.CanOpenOrExport && vm.ReplayTimesUtc.Count > 0)
        { _replayTimer.Start(); PlayButton.Content = "Pause (2 samples/s)"; }
    }
    private void StopReplay() { _replayTimer.Stop(); PlayButton.Content = "Play (2 samples/s)"; }
    private void Bookmark_Click(object sender, RoutedEventArgs e)
    { if (DataContext is SessionViewModel vm) vm.AddBookmarkCommand.Execute(null); }
    private async void Library_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 1 && e.AddedItems[0] is string path && DataContext is SessionViewModel vm && path != vm.FilePath)
        { StopReplay(); await vm.OpenAsync(path); }
    }
}
