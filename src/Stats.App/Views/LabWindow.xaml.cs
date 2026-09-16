using System.Windows;
using System.IO;
using Microsoft.Win32;
using Stats.App.Helpers;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class LabWindow : Window
{
    public LabWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LabViewModel vm) return;
        var dialog = new SaveFileDialog { Filter = "JSON files (*.json)|*.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, vm.DiagnosticPreview);
            File.Move(temporary, dialog.FileName, true);
        }
        catch (Exception ex) { vm.Error = ex.Message; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
