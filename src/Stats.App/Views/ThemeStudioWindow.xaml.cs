using System.IO;
using System.Windows;
using Microsoft.Win32;
using Stats.App.Helpers;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class ThemeStudioWindow : Window
{
    public ThemeStudioWindow() { InitializeComponent(); DarkTitleBar.Apply(this); }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm) return;
        var dialog = new OpenFileDialog { Filter = "Stats theme (*.stats-theme.json)|*.stats-theme.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 8192) throw new InvalidDataException("Theme file exceeds 8 KB.");
            vm.Set(ThemeDesign.Parse(File.ReadAllText(dialog.FileName)));
        }
        catch (Exception ex) { vm.Error = ex.Message; }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm) return;
        try
        {
            var json = vm.Current().ToJson();
            var dialog = new SaveFileDialog { Filter = "Stats theme (*.stats-theme.json)|*.stats-theme.json", DefaultExt = ".stats-theme.json" };
            if (dialog.ShowDialog(this) != true) return;
            var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, json); File.Move(temporary, dialog.FileName, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            vm.Error = "";
        }
        catch (Exception ex) { vm.Error = ex.Message; }
    }
}
