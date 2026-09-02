using System.ComponentModel;
using System.Windows;
using Stats.App.Helpers;

namespace Stats.App.Views;

public partial class MetricDetailWindow : Window
{
    public bool AllowClose { get; set; }

    public MetricDetailWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }
}
