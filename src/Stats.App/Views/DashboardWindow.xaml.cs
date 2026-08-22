using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class DashboardWindow : Window
{
    /// <summary>Set by App: true only when exiting via tray menu; otherwise close hides to tray.</summary>
    public bool AllowClose { get; set; }

    public DashboardWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not DashboardViewModel vm) return;
            var view = new ListCollectionView(vm.Tiles);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MetricTileViewModel.GroupName)));
            TileList.ItemsSource = view;

            var pickerView = new ListCollectionView(vm.PickerItems);
            pickerView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MetricPickerItem.GroupName)));
            PickerList.ItemsSource = pickerView;
        };
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
}
