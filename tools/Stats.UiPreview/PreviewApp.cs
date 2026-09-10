using System.Windows;

namespace Stats.UiPreview;

/// <summary>The preview's own composition root. Deliberately NOT Stats.App.App: never constructs it, never loads
/// its App.xaml startup URI, never touches its elevation/tray/production-settings paths. Merges the same four
/// production resource dictionaries, in the same order, by pack URI — see Stats.App/App.xaml's merge-order
/// comment — so previews resolve resources identically to production. ShutdownMode is explicit so opening and
/// closing preview windows across a batch run never tears the Application down early; Program.cs shuts it down
/// itself once every requested capture is done.</summary>
public sealed class PreviewApp : Application
{
    private static PreviewApp? _instance;

    public static PreviewApp EnsureCreated()
    {
        if (_instance is not null) return _instance;

        _instance = new PreviewApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Merge order matters (StaticResource resolves in parse order) and must match Stats.App/App.xaml exactly.
        _instance.Resources.MergedDictionaries.Add(Load("Theme.xaml"));
        _instance.Resources.MergedDictionaries.Add(Load("Controls.xaml"));
        _instance.Resources.MergedDictionaries.Add(Load("TileTemplates.xaml"));
        _instance.Resources.MergedDictionaries.Add(Load("AppStyles.xaml"));
        return _instance;

        static ResourceDictionary Load(string name) => new()
        {
            Source = new Uri($"pack://application:,,,/Stats.App;component/Views/{name}", UriKind.Absolute),
        };
    }
}
