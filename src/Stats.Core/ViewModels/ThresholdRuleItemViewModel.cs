using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

/// <summary>One row of <see cref="SettingsViewModel.ThresholdRuleItems"/> — a (Group, Unit) rule's editable
/// <see cref="WarnText"/>/<see cref="CritText"/>. These are plain strings (not <c>float</c>) so a keystroke WPF's
/// default binding converter can't parse — a stray letter, a comma-decimal locale — reaches <see cref="Error"/>
/// instead of silently failing the binding and leaving the row unresponsive (the gap this type used to have:
/// <c>TextBox.Text</c> bound directly to a <c>float</c> property never updates the source on unparseable input, so
/// nothing downstream ever runs). <see cref="LowerIsWorse"/> is a read-only display value (a rule's direction is
/// fixed once it exists; only the per-tile <c>ThresholdDialog</c> lets a metric with no rule pick a direction).
/// Edits go through <see cref="SettingsViewModel"/>'s validation via the constructor callback, using the same
/// <see cref="ThresholdInput.TryParse"/> the per-tile dialog uses: only a parseable, ordered pair writes through to
/// the backing <see cref="Rule"/>, saves, and raises <see cref="SettingsChange.Thresholds"/> — anything else leaves
/// the underlying rule alone and surfaces <see cref="Error"/> instead.</summary>
public sealed partial class ThresholdRuleItemViewModel : ObservableObject
{
    private readonly Action<ThresholdRuleItemViewModel> _onChanged;

    public ThresholdRuleItemViewModel(ThresholdRule rule, Action<ThresholdRuleItemViewModel> onChanged)
    {
        Rule = rule;
        _onChanged = onChanged;
        Group = rule.Group;
        Unit = rule.Unit;
        LowerIsWorse = rule.LowerIsWorse;
        // Assign the generated backing fields directly (not the properties) so construction doesn't trigger the
        // partial On*Changed handlers below and re-validate/re-save a rule that hasn't actually been edited.
        _warnText = Format(rule.Warn);
        _critText = Format(rule.Crit);
    }

    private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The (Group, Unit) rule this row edits. Group/Unit never change after construction — only Warn and
    /// Crit are editable here.</summary>
    public ThresholdRule Rule { get; }

    public MetricGroup Group { get; }
    public string GroupName => Group.ToString();
    public string Unit { get; }
    public bool LowerIsWorse { get; }
    public string DirectionText => LowerIsWorse ? "lower is worse" : "";

    [ObservableProperty] private string _warnText;
    [ObservableProperty] private string _critText;
    [ObservableProperty] private string _error = "";

    partial void OnWarnTextChanged(string value) => _onChanged(this);
    partial void OnCritTextChanged(string value) => _onChanged(this);
}

/// <summary>One (Group, Unit) pair with no rule yet, offered by <see cref="SettingsViewModel.AddableRulePairs"/>.
/// <see cref="ToString"/> is the ComboBox's default display text.</summary>
public sealed record ThresholdRulePairOption(MetricGroup Group, string Unit)
{
    public string DisplayText => $"{Group} ({Unit})";
    public override string ToString() => DisplayText;
}
