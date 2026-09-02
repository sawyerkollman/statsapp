using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

/// <summary>One row of <see cref="SettingsViewModel.ThresholdRuleItems"/> — a (Group, Unit) rule's editable Warn
/// and Crit values. <see cref="LowerIsWorse"/> is a read-only display value (a rule's direction is fixed once it
/// exists; only the per-tile <c>ThresholdDialog</c> lets a metric with no rule pick a direction). Edits go through
/// <see cref="SettingsViewModel"/>'s validation via the constructor callback: only an ordered pair writes through
/// to the backing <see cref="Rule"/>, saves, and raises <see cref="SettingsChange.Thresholds"/> — an invalid edit
/// leaves the underlying rule alone and surfaces <see cref="Error"/> instead.</summary>
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
        _warn = rule.Warn;
        _crit = rule.Crit;
    }

    /// <summary>The (Group, Unit) rule this row edits. Group/Unit never change after construction — only Warn and
    /// Crit are editable here.</summary>
    public ThresholdRule Rule { get; }

    public MetricGroup Group { get; }
    public string GroupName => Group.ToString();
    public string Unit { get; }
    public bool LowerIsWorse { get; }
    public string DirectionText => LowerIsWorse ? "lower is worse" : "";

    [ObservableProperty] private float _warn;
    [ObservableProperty] private float _crit;
    [ObservableProperty] private string _error = "";

    partial void OnWarnChanged(float value) => _onChanged(this);
    partial void OnCritChanged(float value) => _onChanged(this);
}

/// <summary>One (Group, Unit) pair with no rule yet, offered by <see cref="SettingsViewModel.AddableRulePairs"/>.
/// <see cref="ToString"/> is the ComboBox's default display text.</summary>
public sealed record ThresholdRulePairOption(MetricGroup Group, string Unit)
{
    public string DisplayText => $"{Group} ({Unit})";
    public override string ToString() => DisplayText;
}
