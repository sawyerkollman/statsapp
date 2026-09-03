using System.ComponentModel;
using System.Windows;
using Stats.App.Helpers;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class PeaksWindow : Window
{
    public bool AllowClose { get; set; }

    public PeaksWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PeaksViewModel vm) return;
        try
        {
            Clipboard.SetText(vm.ToTsv());
            vm.CopyError = "";
        }
        catch (Exception ex)
        {
            vm.CopyError = $"Copy failed: {ex.Message}";
        }
    }
}
