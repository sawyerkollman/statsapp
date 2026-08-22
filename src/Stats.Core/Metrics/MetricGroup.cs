namespace Stats.Core.Metrics;

// Append only: ThresholdRule serializes this enum (as a string, via JsonStringEnumConverter) and
// DashboardViewModel.GroupOrder assumes every member is listed.
public enum MetricGroup { Cpu, Gpu, Memory, Storage, Network, Game, Motherboard, Cooler }
