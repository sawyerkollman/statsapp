using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Processes;

namespace Stats.Core.ViewModels;

public sealed partial class ProcessRowViewModel : ObservableObject
{
    public ProcessRowViewModel(ProcessGroup group) { Key = group.Name; Update(group); }
    public string Key { get; }
    public ProcessGroup Group { get; private set; } = null!;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _nameToolTip = "";
    [ObservableProperty] private string _cpuText = "";
    [ObservableProperty] private string _gpuText = "";
    [ObservableProperty] private string _memoryText = "";
    [ObservableProperty] private string _memoryToolTip = "";

    public void Update(ProcessGroup group)
    {
        Group = group;
        Name = group.PidCount > 1 ? $"{group.Name} \u00d7{group.PidCount}" : group.Name;
        NameToolTip = $"{group.Name} \u2014 {group.PidCount} process{(group.PidCount == 1 ? "" : "es")}";
        CpuText = ProcessFormat.Percent(group.CpuPercent);
        GpuText = ProcessFormat.Percent(group.GpuPercent);
        MemoryText = ProcessFormat.Bytes(group.WorkingSetBytes);
        MemoryToolTip = $"Working set {ProcessFormat.Bytes(group.WorkingSetBytes)} \u00b7 Private {ProcessFormat.Bytes(group.PrivateBytes)}";
    }
}

public sealed partial class ProcessListViewModel : ObservableObject
{
    public const int TopN = 12;
    public const string SamplingText = "Sampling processes\u2026";
    public const string OffText = "Process sampling is off \u2014 turn it on under Settings \u2192 Monitoring \u2192 Processes.";

    public ObservableCollection<ProcessRowViewModel> Rows { get; } = new();
    [ObservableProperty] private ProcessSortColumn _sortColumn = ProcessSortColumn.Cpu;
    [ObservableProperty] private bool _isGpuAvailable;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isUnavailable;
    [ObservableProperty] private string _statusText = SamplingText;
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _copyError = "";

    public bool IsSortedByCpu => SortColumn == ProcessSortColumn.Cpu;
    public bool IsSortedByMemory => SortColumn == ProcessSortColumn.Memory;
    public bool IsSortedByGpu => SortColumn == ProcessSortColumn.Gpu;
    public bool HasSummary => SummaryText.Length > 0;
    public bool HasCopyError => CopyError.Length > 0;
    public ProcessListSnapshot? Latest { get; private set; }

    partial void OnSortColumnChanged(ProcessSortColumn value)
    {
        OnPropertyChanged(nameof(IsSortedByCpu));
        OnPropertyChanged(nameof(IsSortedByMemory));
        OnPropertyChanged(nameof(IsSortedByGpu));
        Reapply();
    }
    partial void OnSummaryTextChanged(string value) => OnPropertyChanged(nameof(HasSummary));
    partial void OnCopyErrorChanged(string value) => OnPropertyChanged(nameof(HasCopyError));

    [RelayCommand]
    private void SortBy(ProcessSortColumn column)
    {
        if (column != ProcessSortColumn.Gpu || IsGpuAvailable) SortColumn = column;
    }

    public void Apply(ProcessListSnapshot snapshot)
    {
        if (!IsEnabled) return;
        Latest = snapshot;
        if (snapshot.IsUnavailable)
        {
            Rows.Clear();
            IsUnavailable = true;
            SummaryText = "";
            StatusText = $"Process list unavailable \u2014 {snapshot.UnavailableReason}";
            return;
        }
        IsUnavailable = false;
        IsGpuAvailable = snapshot.GpuAvailable;
        if (!IsGpuAvailable && SortColumn == ProcessSortColumn.Gpu) SortColumn = ProcessSortColumn.Cpu;
        else Reapply();
        SummaryText = $"Showing {Rows.Count} of {snapshot.Groups.Count} processes \u00b7 updated {snapshot.TimestampUtc.ToLocalTime():HH:mm:ss}";
        StatusText = Rows.Count == 0 ? "No processes readable" : SamplingText;
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        if (enabled) { StatusText = SamplingText; return; }
        Latest = null;
        Rows.Clear();
        SummaryText = "";
        IsUnavailable = false;
        StatusText = OffText;
    }

    /// <summary>Clears the last visible sample while a resumed sampler primes a fresh CPU delta.</summary>
    public void BeginSampling()
    {
        if (!IsEnabled) return;
        Latest = null;
        Rows.Clear();
        SummaryText = "";
        IsUnavailable = false;
        IsGpuAvailable = false;
        StatusText = SamplingText;
    }

    public string ToTsv()
    {
        var lines = new List<string> { "Process\tProcesses\tCPU %\tGPU %\tWorking set MB\tPrivate MB" };
        lines.AddRange(Rows.Select(row => string.Join('\t', row.Group.Name, row.Group.PidCount,
            ProcessFormat.PercentTsv(row.Group.CpuPercent), ProcessFormat.PercentTsv(row.Group.GpuPercent),
            ProcessFormat.Megabytes(row.Group.WorkingSetBytes), ProcessFormat.Megabytes(row.Group.PrivateBytes))));
        return string.Join('\n', lines);
    }

    private void Reapply()
    {
        if (Latest is null || Latest.IsUnavailable) return;
        var target = Latest.Groups.OrderByDescending(group => SortColumn switch
        {
            ProcessSortColumn.Memory => group.WorkingSetBytes,
            ProcessSortColumn.Gpu => group.GpuPercent ?? -1,
            _ => group.CpuPercent ?? -1
        }).ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase).Take(TopN).ToList();
        for (var i = 0; i < target.Count; i++)
        {
            var group = target[i];
            if (i < Rows.Count && string.Equals(Rows[i].Key, group.Name, StringComparison.OrdinalIgnoreCase)) Rows[i].Update(group);
            else
            {
                var existing = Rows.Select((row, index) => (row, index)).FirstOrDefault(x => x.index > i && string.Equals(x.row.Key, group.Name, StringComparison.OrdinalIgnoreCase));
                if (existing.row is not null) { Rows.Move(existing.index, i); Rows[i].Update(group); }
                else Rows.Insert(i, new ProcessRowViewModel(group));
            }
        }
        while (Rows.Count > target.Count) Rows.RemoveAt(Rows.Count - 1);
    }
}
