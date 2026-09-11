using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Stats.App.Helpers;
using Stats.UiPreview.Fixtures;
using Stats.UiPreview.Views;

namespace Stats.UiPreview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // PREVIEW_HARNESS.md: force a fixed culture on the thread and record it in every sidecar.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        var listener = BindingErrorListener.Install();

        try
        {
            if (args.Contains("--interactive")) return RunInteractive(args);

            var batchIndex = Array.IndexOf(args, "--batch");
            if (batchIndex >= 0)
            {
                if (batchIndex + 1 >= args.Length) throw new ArgumentException("--batch requires a manifest path.");
                return RunBatch(args[batchIndex + 1], listener);
            }

            var spec = ParseSpec(args);
            var runRoot = NewRunRoot();
            var outcomes = CaptureHost.Run(spec, runRoot, listener);
            foreach (var o in outcomes)
                Console.WriteLine($"Captured {o.OutputPath} ({o.PhysicalWidth}x{o.PhysicalHeight}) warnings={o.Warnings.Count}");
            Shutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Preview capture failed: " + ex);
            Shutdown();
            return 1;
        }
    }

    private static void Shutdown()
    {
        try { Application.Current?.Shutdown(); } catch (Exception) { /* already shut down */ }
    }

    private static string NewRunRoot() =>
        Path.Combine(Path.GetTempPath(), "Stats.UiPreview", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);

    private static CaptureSpec ParseSpec(string[] args)
    {
        var spec = new CaptureSpec();
        for (int i = 0; i < args.Length; i++)
        {
            string Next()
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[i]}");
                return args[++i];
            }

            switch (args[i])
            {
                case "--scenario": spec.Scenario = Next(); break;
                case "--view": spec.View = Next(); break;
                case "--substate": spec.Substate = Next(); break;
                case "--theme": spec.Theme = Next(); break;
                case "--accent": spec.Accent = Next(); break;
                case "--width": spec.Width = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--height": spec.Height = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--ui-scale": spec.UiScale = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--output": spec.Output = Next(); break;
                case "--settings": spec.Settings = Next(); break;
                case "--method": spec.Method = Next(); break;
                case "--interactive": break; // handled by caller before ParseSpec is reached
                default: throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }
        if (!Scenarios.Names.Contains(spec.Scenario))
            throw new ArgumentException($"Unknown --scenario '{spec.Scenario}'. Valid: {string.Join(", ", Scenarios.Names)}");
        if (spec.Method is not ("screen" or "rtb"))
            throw new ArgumentException($"Unknown --method '{spec.Method}'. Valid: screen, rtb");
        return spec;
    }

    // ---- batch ----

    private static int RunBatch(string manifestPath, BindingErrorListener listener)
    {
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Batch manifest not found.", manifestPath);
        var json = File.ReadAllText(manifestPath);
        var specs = JsonSerializer.Deserialize<List<CaptureSpec>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Batch manifest did not deserialize to a list of captures.");

        var runRoot = NewRunRoot();
        int total = 0, failed = 0;
        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var subRoot = Path.Combine(runRoot, $"{i:D3}-{spec.Scenario}-{spec.View}");
            try
            {
                var outcomes = CaptureHost.Run(spec, subRoot, listener);
                foreach (var o in outcomes)
                {
                    total++;
                    Console.WriteLine($"[{i + 1}/{specs.Count}] {o.OutputPath} ({o.PhysicalWidth}x{o.PhysicalHeight}) warnings={o.Warnings.Count}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"[{i + 1}/{specs.Count}] FAILED scenario={spec.Scenario} view={spec.View} substate={spec.Substate}: {ex.Message}");
            }
        }
        Console.WriteLine($"Batch complete: {total} capture(s) written, {failed} entr{(failed == 1 ? "y" : "ies")} failed.");
        Shutdown();
        return failed == 0 ? 0 : 1;
    }

    // ---- interactive ----

    private static int RunInteractive(string[] args)
    {
        var spec = ParseSpec(args.Where(a => a != "--interactive").ToArray());
        var runRoot = NewRunRoot();
        Directory.CreateDirectory(runRoot);
        var app = PreviewApp.EnsureCreated();
        ThemeManager.Apply(spec.Theme, spec.Accent);

        var composition = PreviewComposition.Build(spec.Scenario, runRoot, spec.SubstateList);
        var window = CaptureHost.BuildWindowForInteractive(spec, composition);
        window.Title += " [preview] [interactive]";
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = 40; window.Top = 40;
        window.Show();
        window.Activate();

        // Seeded tick timer: re-applies a small deterministic wobble around each metric's current value every
        // second, purely so the interactive host visibly "lives" — captured screenshots never use this path.
        var rng = new Random(TimeSeries.Seed);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            var values = new Dictionary<string, float?>();
            foreach (var d in composition.Definitions)
            {
                composition.Store.TryGet(d.Id, out var h);
                values[d.Id] = h?.Current is float cur ? cur + (float)((rng.NextDouble() * 2 - 1) * 0.5) : h?.Current;
            }
            composition.Store.Apply(new Core.Sensors.SensorSnapshot(values, DateTime.UtcNow));
            composition.Dashboard.RefreshAll();
            composition.Overlay.RefreshAll();
        };
        timer.Start();

        window.Closed += (_, _) =>
        {
            timer.Stop();
            app.Shutdown();
        };
        app.Run(window);
        return 0;
    }
}
