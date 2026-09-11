using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    /// <summary>Profile menu (DESIGN.md §6): a code-behind ContextMenu, same pattern as DashboardWindow's View
    /// button, holding the two commands moved out of the always-visible profile row. Delete's header/parameter
    /// snapshot the currently selected profile at open time; the generated DeleteProfileCommand already refuses
    /// a null name (its CanExecute), so the item disables itself when nothing is selected.</summary>
    private void ProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FansViewModel vm || sender is not FrameworkElement target) return;
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        var name = vm.SelectedProfileName;
        var delete = new MenuItem
        {
            Header = name is null ? "Delete" : $"Delete \"{name}\"",
            Command = vm.DeleteProfileCommand,
            CommandParameter = name,
        };
        var createDefaults = new MenuItem { Header = "Create default profiles" };
        createDefaults.Click += (_, _) => vm.CreateDefaultProfilesCommand.Execute(null);
        menu.Items.Add(delete);
        menu.Items.Add(createDefaults);
        menu.IsOpen = true;
    }
}
