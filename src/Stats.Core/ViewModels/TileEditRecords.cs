using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

// Small parameter records packing (metric id, new value) into a single argument so DashboardViewModel's tile
// operations can be [RelayCommand]s (CommunityToolkit's generator only binds a single CommandParameter) — see
// docs/superpowers/specs/2026-09-02-v1.8-monitoring-depth-design.md §7.

/// <summary>Parameter for <see cref="DashboardViewModel.SetTileKindEditCommand"/>.</summary>
public sealed record TileKindEdit(string Id, TileKind Kind);

/// <summary>Parameter for <see cref="DashboardViewModel.SetTileSizeEditCommand"/>.</summary>
public sealed record TileSizeEdit(string Id, TileSize Size);

/// <summary>Parameter for <see cref="DashboardViewModel.SetTileMaxEditCommand"/>.</summary>
public sealed record TileMaxEdit(string Id, float? Max);

/// <summary>Parameter for <see cref="DashboardViewModel.RenameTileEditCommand"/>.</summary>
public sealed record TileRenameEdit(string Id, string? Name);

/// <summary>Parameter for <see cref="DashboardViewModel.SetTileThresholdEditCommand"/>.</summary>
public sealed record TileThresholdEdit(string Id, ThresholdRule? Rule);
