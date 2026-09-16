using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Stats.App.Helpers;
using Stats.App.Views;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Views;

/// <summary>Turns one <see cref="CaptureSpec"/> + <see cref="PreviewComposition"/> into an actual on-screen WPF
/// window, waits for a settled render, drives any substate that needs a real popup/menu/dropdown, captures a PNG
/// (screen or RenderTargetBitmap per --method), writes its sidecar, and closes the window. The only file in this
/// project that constructs a real Stats.App view.</summary>
public static class CaptureHost
{
    public sealed record Outcome(string OutputPath, int PhysicalWidth, int PhysicalHeight, IReadOnlyList<string> Warnings);

    private static readonly (string Theme, string? Accent)[] ThemeCycleSequence =
    {
        ("Dark Amber", null), ("Dark Blue", null), ("Dark Green", null),
        ("Dark Purple", null), ("Light", null), ("Dark Amber", "#3FBFBF"),
        ("Synthwave", null), ("Outrun", null), ("Midnight", null), ("Light", null),
    };

    public static List<Outcome> Run(CaptureSpec spec, string tempRoot, BindingErrorListener listener)
    {
        SubstateCatalog.Validate(spec.View, spec.SubstateList);
        if (string.IsNullOrEmpty(spec.Output))
            throw new ArgumentException("--output is required for a capture (or use --batch/--interactive).");
        listener.TakeAndClear(); // begin this capture's binding-error window before any composition or view construction

        bool themeCycle = spec.SubstateList.Contains("theme-cycle");
        var buildSubstates = spec.SubstateList
            .Where(s => spec.View != "lab" && s != "theme-cycle" && !s.StartsWith("category-", StringComparison.Ordinal))
            .Select(s => spec.View == "settings" && s == "update-error" ? "settings-update-error" : s)
            .ToArray();

        PreviewApp.EnsureCreated();
        var composition = PreviewComposition.Build(spec.Scenario, tempRoot, buildSubstates, spec.Settings);

        ThemeManager.Apply(spec.Theme, spec.Accent);
        // Graph effects (T3 of docs/superpowers/plans/2026-09-11-graph-effects.md): hold GraphEffects off for the
        // window's first bind/layout pass, below, so Sparkline/HistoryChart's last-value pulse — a real-time
        // DoubleAnimation started the first time a control's Values binding changes from null to the fixture's
        // initial history, if GraphStyle.Motion is already true at that moment — never starts. This harness has no
        // way to fast-forward WPF's animation clock, so a capture taken shortly after would otherwise show a
        // mid-flight pulse ring. GraphStyle.Apply(composition.Settings) below (after the first settle) then
        // applies the fixture's real SmoothLines/GraphEffects before anything is captured; that second apply only
        // fires GraphStyle.Changed (InvalidateVisual — no animation), since Values doesn't change again. Bars/
        // gauges are unaffected either way — LevelBar/ArcGauge already skip easing on their own first assignment.
        GraphStyle.Apply(new AppSettings { SmoothLines = composition.Settings.SmoothLines, GraphEffects = false });
        var window = BuildWindow(spec, composition);
        window.Title += " [preview]";
        window.Topmost = true;
        if (spec.View is not ("threshold-dialog" or "input-dialog" or "overlay"))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = 40;
            window.Top = 40;
            window.Width = spec.Width;
            window.Height = spec.Height;
        }
        window.Show();
        DispatcherUtil.WaitForSettled(window, TimeSpan.FromSeconds(5));
        window.Activate();
        DispatcherUtil.WaitFrames();

        GraphStyle.Apply(composition.Settings); // now safe: the initial Values bind already happened at rest
        DispatcherUtil.WaitFrames();

        ApplyVisualSubstates(window, spec, composition);
        DispatcherUtil.WaitFrames();

        var results = new List<Outcome>();
        try
        {
            if (themeCycle)
            {
                int i = 0;
                foreach (var (theme, accent) in ThemeCycleSequence)
                {
                    ThemeManager.Apply(theme, accent);
                    composition.Dashboard.RaiseSeverityRefresh();
                    DispatcherUtil.WaitFrames(10);
                    var outPath = ThemeCyclePath(spec.Output!, i, theme, accent);
                    results.Add(CaptureAndWrite(window, spec, composition, listener, outPath, theme, accent));
                    i++;
                }
            }
            else
            {
                results.Add(CaptureAndWrite(window, spec, composition, listener, spec.Output!, spec.Theme, spec.Accent));
            }
        }
        finally
        {
            AllowCloseIfApplicable(window);
            window.Close();
            DispatcherUtil.WaitFrames();
            composition.Commands.WriteTo(Path.Combine(tempRoot, "commands.log"));
        }
        return results;
    }

    private static string ThemeCyclePath(string basePath, int index, string theme, string? accent)
    {
        var dir = Path.GetDirectoryName(basePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        var slug = theme.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant() + (accent is null ? "" : "-custom");
        return Path.Combine(dir, $"{name}-{index}-{slug}{ext}");
    }

    private static void AllowCloseIfApplicable(Window window)
    {
        switch (window)
        {
            case DashboardWindow d: d.AllowClose = true; break;
            case FansWindow f: f.AllowClose = true; break;
            case PeaksWindow p: p.AllowClose = true; break;
            case MetricDetailWindow m: m.AllowClose = true; break;
            case ComparisonWindow c: c.AllowClose = true; break;
            case SessionWindow s: s.AllowClose = true; break;
        }
    }

    /// <summary>Same window construction a capture uses, exposed for --interactive (no capture/sidecar follows).</summary>
    public static Window BuildWindowForInteractive(CaptureSpec spec, PreviewComposition c)
    {
        ThemeManager.Apply(spec.Theme, spec.Accent);
        GraphStyle.Apply(c.Settings); // no capture timing constraint here — live pulses are fine interactively
        var window = BuildWindow(spec, c);
        if (spec.View is not ("threshold-dialog" or "input-dialog" or "overlay"))
        {
            window.Width = spec.Width;
            window.Height = spec.Height;
        }
        return window;
    }

    // ---- window construction ----

    private static Window BuildWindow(CaptureSpec spec, PreviewComposition c) => spec.View switch
    {
        "dashboard" => BuildDashboard(spec, c),
        "picker" => BuildDashboard(spec, c, openFlyout: true, tab: 0),
        "settings" => BuildDashboard(spec, c, openFlyout: true, tab: 1),
        "fans" => new FansWindow { DataContext = c.Fans },
        "peaks" => new PeaksWindow { DataContext = c.Peaks },
        "alerts" => new PeaksWindow { DataContext = c.Peaks },
        "processes" => new PeaksWindow { DataContext = c.Peaks },
        "comparison" => new ComparisonWindow { DataContext = c.Comparison },
        "sessions" => new SessionWindow { DataContext = c.Sessions },
        "theme-studio" => new ThemeStudioWindow { DataContext = new ThemeStudioViewModel(c.Settings, () => { }) },
        "scenes" => BuildScenes(c),
        "lab" => BuildLab(spec, c),
        "details" => BuildDetails(spec, c),
        "overlay" => new OverlayWindow { DataContext = c.Overlay, Opacity = c.Settings.OverlayOpacity },
        "threshold-dialog" => BuildThresholdDialog(spec, c),
        "input-dialog" => BuildInputDialog(),
        _ => throw new ArgumentException($"Unknown --view '{spec.View}'."),
    };

    private static Window BuildScenes(PreviewComposition c)
    {
        var vm = new SceneComposerViewModel(c.Settings, c.Definitions, () => { });
        vm.Name = "Gaming cockpit"; vm.SaveCurrentCommand.Execute(null);
        return new SceneComposerWindow { DataContext = vm };
    }

    private static Window BuildLab(CaptureSpec spec, PreviewComposition c)
    {
        var vm = new LabViewModel(Path.Combine(c.TempRoot, "beta-lab"), c.Definitions);
        vm.AddGameCommand.Execute(null); vm.SelectedGame!.ExecutableBaseName = "simulated-game";
        vm.AddRuleCommand.Execute(null);
        vm.SelectedRule!.FirstMetricId = c.Definitions.FirstOrDefault()?.Id ?? "";
        vm.SelectedRule.SecondMetricId = c.Definitions.Skip(1).FirstOrDefault()?.Id ?? "";
        vm.SelectedRule.FirstThreshold = 90; vm.SelectedRule.SecondThreshold = 80;
        vm.AddTimeline("bookmark", "Simulated warm-up complete", TimeSeries.FixedTimeUtc);
        vm.SetDiagnostics("preview", "simulated Windows", ".NET 8", true);
        var window = new LabWindow { DataContext = vm };
        window.Closed += (_, _) => vm.Dispose();
        var index = Array.IndexOf(new[] { "gaming", "compound", "history", "timeline", "diagnostics" }, spec.Substate);
        ((TabControl)window.FindName("LabTabs")).SelectedIndex = Math.Max(0, index);
        return window;
    }

    private static DashboardWindow BuildDashboard(CaptureSpec spec, PreviewComposition c, bool openFlyout = false, int tab = 0)
    {
        var window = new DashboardWindow { DataContext = c.Dashboard, Settings = c.Settings };
        c.Dashboard.UiScale = spec.UiScale; // --ui-scale drives the same LayoutTransform Settings > Appearance does
        if (openFlyout) { c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = tab; }
        c.Dashboard.RefreshAll(); // production refreshes after the flyout opens; fills the picker's live "Now" column
        return window;
    }

    private static MetricDetailWindow BuildDetails(CaptureSpec spec, PreviewComposition c)
    {
        var substate = spec.SubstateList.FirstOrDefault();
        var def = substate switch
        {
            "gap" => c.Definitions.FirstOrDefault(d => c.Store.TryGet(d.Id, out var h) && h.Current is null),
            "long-unit" => c.Definitions.FirstOrDefault(d => d.Id == "details.longunit"),
            "thresholds" => c.Definitions.FirstOrDefault(d => d.Group == MetricGroup.Cpu && d.Unit == "°C"),
            _ => null,
        } ?? c.Definitions.FirstOrDefault(d => c.Settings.DashboardMetrics.Contains(d.Id)) ?? c.Definitions.First();
        c.Store.TryGet(def.Id, out var history);
        var vm = new MetricDetailViewModel(def, history, c.Settings);
        return new MetricDetailWindow { DataContext = vm };
    }

    private static ThresholdDialog BuildThresholdDialog(CaptureSpec spec, PreviewComposition c)
    {
        var dlg = new ThresholdDialog();
        var substate = spec.SubstateList.FirstOrDefault() ?? "valid";
        switch (substate)
        {
            case "invalid":
                dlg.Initialize("Tctl/Tdie", "°C", null, null);
                // Set directly (before Show/render) rather than via a simulated OK click after Show — a synthetic
                // ButtonBase.Click RaiseEvent on an IsDefault button was observed to run Ok_Click correctly (the
                // TextBox/ErrorText properties were provably updated) but the *composited* frame still showed the
                // pre-click state, even after extra dispatcher/DWM flushing. Setting the same end state up front
                // sidesteps that timing issue entirely.
                dlg.WarnBox.Text = "not-a-number";
                dlg.CritBox.Text = "10";
                dlg.ErrorText.Text = "Warn must be a number";
                break;
            case "lower-is-worse":
                dlg.Initialize("Simulated FPS", "fps", null, new ThresholdRule { Warn = 30, Crit = 15, LowerIsWorse = true });
                break;
            default: // "valid"
                dlg.Initialize("Tctl/Tdie", "°C", new ThresholdRule { Group = MetricGroup.Cpu, Unit = "°C", Warn = 85, Crit = 92 }, null);
                break;
        }
        return dlg;
    }

    private static InputDialog BuildInputDialog()
    {
        var dlg = new InputDialog { Title = "Rename tile" };
        dlg.PromptText.Text = "Display name (blank = sensor name):";
        dlg.Input.Text = "Tctl/Tdie";
        return dlg;
    }

    // ---- substates that need a real visual tree ----

    /// <summary>--substate category-<name> for --view settings (T5): the category name maps 1:1 to the nested
    /// TabControl's TabItem order (Appearance, Monitoring, Alerts, Overlay, System) in DashboardWindow.xaml.</summary>
    private static readonly string[] SettingsCategoryOrder = { "appearance", "monitoring", "alerts", "overlay", "system" };

    private static void ApplyVisualSubstates(Window window, CaptureSpec spec, PreviewComposition c)
    {
        foreach (var substate in spec.SubstateList)
        {
            if (substate.StartsWith("category-", StringComparison.Ordinal) && window is DashboardWindow categoryWindow)
            {
                SelectSettingsCategory(categoryWindow, substate["category-".Length..]);
                continue;
            }
            switch (substate)
            {
                case "tile-menu" when window is DashboardWindow:
                    OpenFirstTileContextMenu(window);
                    break;
                case "view-menu" when window is DashboardWindow:
                    OpenViewMenu(window);
                    break;
                case "theme-dropdown" when window is DashboardWindow:
                    DispatcherUtil.WaitFrames();
                    OpenThemeDropdown(window, c);
                    break;
                case "layout-resize-grip" when window is DashboardWindow:
                    FocusFirstFreeTileContainer(window);
                    break;
            }
        }
        if (spec.View == "alerts" && window is PeaksWindow pw) SelectAlertsTab(pw);
        if (spec.View == "processes" && window is PeaksWindow processWindow) SelectProcessesTab(processWindow);
    }

    private static void SelectSettingsCategory(Window window, string category)
    {
        var tabs = VisualTreeUtil.FirstDescendant<TabControl>(window, t => t.Name == "SettingsCategoryTabs");
        if (tabs is null) return;
        var index = Array.IndexOf(SettingsCategoryOrder, category.ToLowerInvariant());
        if (index < 0 || index >= tabs.Items.Count)
            throw new ArgumentException($"--substate 'category-{category}' does not match a settings category. Valid: {string.Join(", ", SettingsCategoryOrder)}");
        tabs.SelectedIndex = index;
        DispatcherUtil.WaitFrames();
    }

    private static void OpenFirstTileContextMenu(Window window)
    {
        // A DataTemplate's own root gets DataContext == the templated item — including the generated
        // ItemsControl container itself (a ContentPresenter) in this WPF version, so a broad search finds it.
        var element = VisualTreeUtil.FirstDescendant<FrameworkElement>(window, e => e.DataContext is MetricTileViewModel);
        if (element is null) return;

        // ContextMenu's default Placement is MousePoint (opens at the real OS cursor, not at PlacementTarget) —
        // move the actual cursor onto the tile first so the menu Tile_MouseRightButtonUp opens lands within the
        // captured window rect instead of wherever the environment's cursor happened to be parked.
        var center = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        Native.MoveCursorTo((int)center.X, (int)center.Y);
        DispatcherUtil.WaitFrames();

        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        {
            RoutedEvent = UIElement.MouseRightButtonUpEvent,
            Source = element,
        });
        DispatcherUtil.WaitFrames();
    }

    private static void OpenThemeDropdown(Window window, PreviewComposition c)
    {
        var combo = VisualTreeUtil.FirstDescendant<ComboBox>(window,
            b => ReferenceEquals(b.ItemsSource, c.SettingsVm.ThemePresetNames));
        if (combo is null) throw new InvalidOperationException("Theme preset picker was not found in the preview visual tree.");
        combo.BringIntoView(); // the Settings tab is a ScrollViewer — the preset picker sits below the fold
        DispatcherUtil.WaitFrames(5);
        // BringIntoView scrolls the minimum distance, landing the combo flush with the viewport's bottom edge —
        // exactly where its downward-opening dropdown has no room. Scroll a bit further so it sits with headroom.
        if (VisualTreeUtil.FirstAncestor<ScrollViewer>(combo) is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(Math.Min(sv.ScrollableHeight, sv.VerticalOffset + 220));
            DispatcherUtil.WaitFrames(5);
        }
        combo.Focus();
        DispatcherUtil.WaitFrames(3);
        var popup = combo.Template.FindName("PART_Popup", combo) as System.Windows.Controls.Primitives.Popup;
        // Queue draining is not elapsed animation time; capture the popup's settled state, not its slide-in.
        if (popup is not null) popup.PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None;
        combo.IsDropDownOpen = true;
        DispatcherUtil.WaitFrames(10);
        if (!combo.IsDropDownOpen || popup?.Child?.IsVisible != true)
            throw new InvalidOperationException($"Theme dropdown did not open: visible={combo.IsVisible}, focused={combo.IsKeyboardFocusWithin}, open={combo.IsDropDownOpen}, popup={popup?.IsOpen}.");
    }

    /// <summary>"layout-resize-grip" (docs/superpowers/specs/2026-09-11-tile-resize-design.md "Preview harness"):
    /// pairs with "layout-free" (applied first, at the composition level in PreviewComposition.ApplySubstate) —
    /// finds the Free canvas (<c>x:Name="FreeTilesList"</c> in DashboardWindow.xaml, a TileCanvasItemsControl) and
    /// keyboard-focuses its first tile container, so that container's IsKeyboardFocusWithin trigger shows its
    /// resize grip (the grip is otherwise only visible on IsMouseOver, which this headless harness cannot fake).</summary>
    private static void FocusFirstFreeTileContainer(Window window)
    {
        var freeList = VisualTreeUtil.FirstDescendant<FrameworkElement>(window, e => e.Name == "FreeTilesList");
        if (freeList is null) return;
        var container = VisualTreeUtil.FirstDescendant<ContentControl>(freeList, e => e.DataContext is MetricTileViewModel);
        if (container is null) return;
        Keyboard.Focus(container);
        DispatcherUtil.WaitFrames();
    }

    private static void SelectAlertsTab(Window window)
    {
        var tabs = VisualTreeUtil.FirstDescendant<TabControl>(window);
        if (tabs is null || tabs.Items.Count < 2) return;
        tabs.SelectedIndex = 1;
        DispatcherUtil.WaitFrames();
    }

    private static void OpenViewMenu(Window window)
    {
        if (window.FindName("ViewButton") is not Button button) return;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        DispatcherUtil.WaitFrames();
    }

    private static void SelectProcessesTab(Window window)
    {
        var tabs = VisualTreeUtil.FirstDescendant<TabControl>(window);
        if (tabs is not null && tabs.Items.Count > 2) tabs.SelectedIndex = 2;
        DispatcherUtil.WaitFrames();
    }

    // ---- capture + sidecar ----

    private static Outcome CaptureAndWrite(Window window, CaptureSpec spec, PreviewComposition c, BindingErrorListener listener,
        string outputPath, string theme, string? accent)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var (physicalWidth, physicalHeight, dpiX, dpiY) = spec.Method == "rtb"
            ? Screenshot.CaptureRenderTargetBitmap(window, outputPath)
            : Screenshot.CaptureScreen(window, outputPath);

        var warnings = listener.TakeAndClear();
        var sidecar = BuildSidecar(spec, c, outputPath, physicalWidth, physicalHeight, dpiX, dpiY, theme, accent, warnings);
        sidecar.WriteTo(Path.ChangeExtension(outputPath, ".json"));
        return new Outcome(outputPath, physicalWidth, physicalHeight, warnings);
    }

    private static Sidecar BuildSidecar(CaptureSpec spec, PreviewComposition c, string outputPath,
        int physicalWidth, int physicalHeight, double dpiX, double dpiY, string theme, string? accent, List<string> warnings)
    {
        var (commit, dirtyDiff) = GitInfo.Read();
        var specForCommand = new CaptureSpec
        {
            Scenario = spec.Scenario, View = spec.View, Substate = spec.Substate, Theme = theme, Accent = accent,
            Width = spec.Width, Height = spec.Height, UiScale = spec.UiScale, Output = outputPath, Settings = spec.Settings, Method = spec.Method,
        };
        return new Sidecar
        {
            SourceCommit = commit,
            DirtyDiffIdentity = dirtyDiff,
            Scenario = spec.Scenario,
            Substate = spec.Substate,
            View = spec.View,
            Theme = theme,
            Accent = accent,
            LogicalWidth = spec.Width,
            LogicalHeight = spec.Height,
            PhysicalWidth = physicalWidth,
            PhysicalHeight = physicalHeight,
            DpiX = dpiX,
            DpiY = dpiY,
            UiScale = spec.UiScale,
            Culture = CultureInfo.CurrentCulture == CultureInfo.InvariantCulture ? "Invariant" : CultureInfo.CurrentCulture.Name,
            TimeZoneId = TimeZoneInfo.Local.Id,
            FixtureTimeUtc = TimeSeries.FixedTimeUtc.ToString("O"),
            FixtureSeed = TimeSeries.Seed,
            LaunchCommand = specForCommand.CommandLine(),
            ScreenshotMethod = spec.Method,
            Warnings = warnings,
            SimulatedServices = Sidecar.AllSimulated.ToList(),
        };
    }
}
