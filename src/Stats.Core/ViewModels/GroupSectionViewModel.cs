using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;

namespace Stats.Core.ViewModels;

/// <summary>One collapsible dashboard group (Cpu/Gpu/…): its tiles in user order, plus the core matrix for Cpu.</summary>
public sealed partial class GroupSectionViewModel : ObservableObject
{
    private readonly Action<string, bool> _onExpandedChanged;

    public GroupSectionViewModel(MetricGroup group, bool isExpanded, Action<string, bool> onExpandedChanged)
    {
        Group = group;
        _isExpanded = isExpanded;
        _onExpandedChanged = onExpandedChanged;
    }

    public MetricGroup Group { get; }
    public string Name => Group.ToString();
    public ObservableCollection<MetricTileViewModel> Tiles { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private CoreMatrixViewModel? _coreMatrix;

    partial void OnIsExpandedChanged(bool value) => _onExpandedChanged(Name, value);
}
