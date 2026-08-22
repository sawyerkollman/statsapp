using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;

namespace Stats.Core.ViewModels;

public sealed partial class MetricPickerItem : ObservableObject
{
    public MetricPickerItem(MetricDefinition definition, bool isChecked, bool isOnOverlay, string displayName)
    {
        Definition = definition;
        _isChecked = isChecked;
        _isOnOverlay = isOnOverlay;
        _displayName = displayName;
    }

    public MetricDefinition Definition { get; }
    public string GroupName => $"{Definition.Group} — {Definition.HardwareName}";

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isOnOverlay;
    /// <summary>Live value shown in the picker; refreshed only while the flyout is open.</summary>
    [ObservableProperty] private string _currentText = "";
}
