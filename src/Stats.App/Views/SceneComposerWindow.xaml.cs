using System.Windows;
using System.IO;
using Microsoft.Win32;
using Stats.App.Helpers;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class SceneComposerWindow : Window
{
    public SceneComposerWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SceneComposerViewModel vm || vm.SelectedScene is null) return;
        if (MessageBox.Show(this, $"Delete scene '{vm.SelectedScene.Name}'?", "Stats", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            vm.DeleteSelectedCommand.Execute(null);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SceneComposerViewModel vm) return;
        var dialog = new SaveFileDialog { Filter = "Stats scene (*.json)|*.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, vm.ExportSelected());
            File.Move(temporary, dialog.FileName, true);
        }
        catch (Exception ex) { vm.Error = ex.Message; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SceneComposerViewModel vm) return;
        var dialog = new OpenFileDialog { Filter = "Stats scene (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1_000_000) throw new InvalidDataException("Scene import exceeds 1 MB.");
            vm.Import(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) { vm.Error = ex.Message; }
    }
}
