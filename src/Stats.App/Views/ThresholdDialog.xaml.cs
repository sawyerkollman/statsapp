using System.Globalization;
using System.Windows;
using Stats.App.Helpers;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.App.Views;

/// <summary>Modal per-tile threshold editor, replacing the free-text <c>InputDialog</c> prompt. Warn/Crit are two
/// numeric fields (validated by the pure <see cref="ThresholdInput.TryParse"/> — bad input never silently
/// discards the existing override). When the metric's (Group, Unit) already has a rule its direction governs the
/// override too (shown as a note, not editable here); when it doesn't, an explicit "Lower is worse" checkbox lets
/// the user pick a direction for this metric alone. <see cref="Clear_Click"/> removes the override outright.</summary>
public partial class ThresholdDialog : Window
{
    private ThresholdRule? _groupRule;

    /// <summary>Set by <see cref="Ok_Click"/> on a valid pair; null when <see cref="Cleared"/> is true or the
    /// dialog was cancelled.</summary>
    public ThresholdRule? Result { get; private set; }
    /// <summary>True when the user clicked Clear — the caller should remove the per-tile override.</summary>
    public bool Cleared { get; private set; }

    public ThresholdDialog()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Loaded += (_, _) => { WarnBox.Focus(); WarnBox.SelectAll(); };
    }

    /// <summary>Must be called before <see cref="Window.ShowDialog()"/> — see <see cref="Show"/>.</summary>
    public void Initialize(string metricName, string unit, ThresholdRule? groupRule, ThresholdRule? existingOverride)
    {
        _groupRule = groupRule;
        MetricNameText.Text = $"{metricName} ({unit})";
        WarnLabel.Text = $"Warn ({unit})";
        CritLabel.Text = $"Crit ({unit})";

        var source = existingOverride ?? groupRule;
        WarnBox.Text = source is not null ? source.Warn.ToString("0.##", CultureInfo.InvariantCulture) : "";
        CritBox.Text = source is not null ? source.Crit.ToString("0.##", CultureInfo.InvariantCulture) : "";

        if (groupRule is not null)
        {
            // The group already governs direction — show it as information, but the checkbox (which would imply
            // the user can change it here) is hidden.
            DirectionNote.Text = groupRule.LowerIsWorse ? "Lower is worse for this metric." : "";
            DirectionNote.Visibility = groupRule.LowerIsWorse ? Visibility.Visible : Visibility.Collapsed;
            LowerIsWorseCheck.Visibility = Visibility.Collapsed;
        }
        else
        {
            DirectionNote.Visibility = Visibility.Collapsed;
            LowerIsWorseCheck.Visibility = Visibility.Visible;
            LowerIsWorseCheck.IsChecked = existingOverride?.LowerIsWorse ?? false;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        bool lowerIsWorse = _groupRule?.LowerIsWorse ?? (LowerIsWorseCheck.IsChecked == true);
        if (!ThresholdInput.TryParse(WarnBox.Text, CritBox.Text, lowerIsWorse, out var rule, out var error))
        {
            ErrorText.Text = error;
            return; // never silently ignored — the dialog stays open with the message shown
        }
        Result = rule;
        Cleared = false;
        DialogResult = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        Cleared = true;
        DialogResult = true;
    }

    /// <summary>Shows the dialog modally. Returns false if cancelled (no change). On true, check
    /// <see cref="Cleared"/> first: if set, remove the override; otherwise apply <see cref="Result"/>.</summary>
    public static bool Show(Window owner, string metricName, string unit, ThresholdRule? groupRule,
        ThresholdRule? existingOverride, out ThresholdRule? result, out bool cleared)
    {
        var dlg = new ThresholdDialog { Owner = owner };
        dlg.Initialize(metricName, unit, groupRule, existingOverride);
        var ok = dlg.ShowDialog() == true;
        result = dlg.Result;
        cleared = dlg.Cleared;
        return ok;
    }
}
