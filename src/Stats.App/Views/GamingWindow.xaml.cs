using System.Windows;
using Stats.App.Helpers;

namespace Stats.App.Views;

public partial class GamingWindow : Window
{
    public GamingWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }
}
