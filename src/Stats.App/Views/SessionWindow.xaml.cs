using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using Stats.App.Helpers;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class SessionWindow : Window
{
    public bool AllowClose { get; set; }
    public SessionWindow() { InitializeComponent(); DarkTitleBar.Apply(this); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var directory = (DataContext as SessionViewModel)?.RecordingDirectory;
        var dialog = new OpenFileDialog { Filter = "Stats sessions (*.stats-session.jsonl)|*.stats-session.jsonl|All files (*.*)|*.*", InitialDirectory = directory };
        if (dialog.ShowDialog(this) == true && DataContext is SessionViewModel vm) vm.Open(dialog.FileName);
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var directory = (DataContext as SessionViewModel)?.RecordingDirectory;
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", DefaultExt = ".csv", InitialDirectory = directory };
        if (dialog.ShowDialog(this) == true && DataContext is SessionViewModel vm) vm.Export(dialog.FileName);
    }
}
