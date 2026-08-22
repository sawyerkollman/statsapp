using System.Windows;
using Stats.App.Helpers;

namespace Stats.App.Views;

public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>Returns the entered text, or null if cancelled.</summary>
    public static string? Show(Window owner, string title, string prompt, string initial)
    {
        var dlg = new InputDialog { Owner = owner, Title = title };
        dlg.PromptText.Text = prompt;
        dlg.Input.Text = initial;
        return dlg.ShowDialog() == true ? dlg.Input.Text : null;
    }
}
