using System.ComponentModel;
using System.Windows;
using Stats.App.Helpers;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class FansWindow : Window
{
    public bool AllowClose { get; set; }

    public FansWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FansViewModel vm) return;
        var result = InputDialog.Show(this, "Save fan profile", "Profile name:", vm.SelectedProfileName ?? "");
        if (string.IsNullOrWhiteSpace(result)) return;
        vm.SaveProfileCommand.Execute(result.Trim());
    }
}
