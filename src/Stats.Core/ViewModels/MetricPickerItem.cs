using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;

namespace Stats.Core.ViewModels;

public sealed partial class MetricPickerItem : ObservableObject
{
    public MetricPickerItem(MetricDefinition definition, bool isChecked, bool isOnOverlay)
    {
        Definition = definition;
        _isChecked = isChecked;
        _isOnOverlay = isOnOverlay;
    }

    public MetricDefinition Definition { get; }
    public string DisplayName => Definition.DisplayName;
    public string GroupName => $"{Definition.Group} — {Definition.HardwareName}";

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isOnOverlay;
}
