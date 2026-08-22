using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Fans;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed record FanSourceOption(string Id, string Label);

/// <summary>One controllable fan row. Setters push desired state to the controller; Refresh pulls live values.</summary>
public sealed partial class FanChannelViewModel : ObservableObject
{
    private readonly FanController _controller;
    private bool _refreshing;

    public FanChannelViewModel(FanChannelView v, FanController controller, IReadOnlyList<FanSourceOption> sourceOptions)
    {
        _controller = controller;
        Id = v.Id;
        Device = v.Device;
        MinPercent = v.MinPercent;
        MaxPercent = v.MaxPercent;
        SourceOptions = sourceOptions;
        _name = v.Name;
        _mode = v.Mode;
        _manualPercent = v.ManualPercent;
        _sourceMetricId = v.SourceMetricId;
        Points = new ObservableCollection<FanPoint>(v.Points);
        Points.CollectionChanged += OnPointsChanged;
        Apply(v);
    }

    public string Id { get; }
    public string Device { get; }
    public float MinPercent { get; }
    public float MaxPercent { get; }
    public IReadOnlyList<FanSourceOption> SourceOptions { get; }
    public ObservableCollection<FanPoint> Points { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private FanMode _mode;
    [ObservableProperty] private float _manualPercent;
    [ObservableProperty] private string? _sourceMetricId;
    [ObservableProperty] private string _rpmText = "—";
    [ObservableProperty] private string _percentText = "—";
    [ObservableProperty] private string _targetText = "—";
    [ObservableProperty] private string _sourceTempText = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _liveTemp = double.NaN;
    [ObservableProperty] private double _liveTarget = double.NaN;

    public bool IsManual => Mode == FanMode.Manual;
    public bool IsCurve => Mode == FanMode.Curve;

    partial void OnNameChanged(string value) { if (!_refreshing) _controller.SetName(Id, value); }
    partial void OnModeChanged(FanMode value)
    {
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(IsCurve));
        if (!_refreshing) _controller.SetMode(Id, value);
    }
    partial void OnManualPercentChanged(float value) { if (!_refreshing) _controller.SetManualPercent(Id, value); }
    partial void OnSourceMetricIdChanged(string? value) { if (!_refreshing) _controller.SetSource(Id, value); }

    private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _controller.TrySetPoints(Id, Points); // invalid intermediate states (e.g. during clear) are ignored
    }

    [RelayCommand]
    private void ResetCurve()
    {
        _controller.ResetCurve(Id);
        ReplacePoints(FanCurve.DefaultPoints);
    }

    /// <summary>Pull live values (and any controller-side changes, e.g. failsafe → Auto) without echoing back.</summary>
    public void Apply(FanChannelView v)
    {
        _refreshing = true;
        try
        {
            if (Name != v.Name) Name = v.Name;
            if (Mode != v.Mode) Mode = v.Mode;
            if (ManualPercent != v.ManualPercent) ManualPercent = v.ManualPercent;
            if (SourceMetricId != v.SourceMetricId) SourceMetricId = v.SourceMetricId;
            if (!Points.SequenceEqual(v.Points)) ReplacePoints(v.Points);
            RpmText = v.Rpm is float r ? $"{r:F0} RPM" : "—";
            PercentText = v.Percent is float p ? $"{p:F0} %" : "—";
            TargetText = v.TargetPercent is float t ? $"{t:F0} %" : "—";
            SourceTempText = v.SourceTemp is float s ? string.Create(CultureInfo.InvariantCulture, $"{s:F1} °C") : "—";
            LiveTemp = v.SourceTemp ?? double.NaN;
            LiveTarget = v.TargetPercent ?? double.NaN;
            StatusText = v.Status switch
            {
                FanChannelStatus.Idle => v.Mode == FanMode.Auto ? "Device control" : "",
                FanChannelStatus.Active => "Active",
                FanChannelStatus.WaitingForSource => "Waiting for temperature…",
                FanChannelStatus.SourceUnavailable => "Source unavailable — device control",
                FanChannelStatus.WriteFailed => "Write failed — check other fan software",
                _ => "",
            };
        }
        finally { _refreshing = false; }
    }

    private void ReplacePoints(IReadOnlyList<FanPoint> pts)
    {
        bool was = _refreshing; _refreshing = true;
        try { Points.Clear(); foreach (var p in pts) Points.Add(p); }
        finally { _refreshing = was; }
    }
}

public sealed partial class FanDeviceGroupViewModel : ObservableObject
{
    public FanDeviceGroupViewModel(string device) => Device = device;
    public string Device { get; }
    public ObservableCollection<FanChannelViewModel> Channels { get; } = new();
}

/// <summary>Fans window: master switch + channels grouped by device.</summary>
public sealed partial class FansViewModel : ObservableObject
{
    private readonly FanController _controller;
    private readonly Dictionary<string, FanChannelViewModel> _byId = new();

    public FansViewModel(FanController controller, IReadOnlyList<MetricDefinition> definitions, AppSettings settings)
    {
        _controller = controller;
        var options = definitions
            .Where(d => d.Unit == "°C")
            .Select(d => new FanSourceOption(d.Id,
                settings.TilePrefs.TryGetValue(d.Id, out var p) && !string.IsNullOrWhiteSpace(p.Name) ? p.Name! : d.DisplayName))
            .ToList();
        foreach (var v in controller.Views())
        {
            var group = Devices.FirstOrDefault(g => g.Device == v.Device);
            if (group is null) { group = new FanDeviceGroupViewModel(v.Device); Devices.Add(group); }
            var ch = new FanChannelViewModel(v, controller, options);
            group.Channels.Add(ch);
            _byId[v.Id] = ch;
        }
        _enabled = controller.Enabled;
    }

    public ObservableCollection<FanDeviceGroupViewModel> Devices { get; } = new();
    public bool HasChannels => _byId.Count > 0;

    [ObservableProperty] private bool _enabled;
    partial void OnEnabledChanged(bool value) => _controller.Enabled = value;

    [RelayCommand]
    private void SetAllAuto()
    {
        foreach (var ch in _byId.Values) ch.Mode = FanMode.Auto;
    }

    public void Refresh()
    {
        foreach (var v in _controller.Views())
            if (_byId.TryGetValue(v.Id, out var ch)) ch.Apply(v);
        if (Enabled != _controller.Enabled) Enabled = _controller.Enabled;
    }
}
