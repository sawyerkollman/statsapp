using Stats.App.Views;
using Stats.Core.ViewModels;

namespace Stats.App;

public partial class App
{
    private OverlayEditorWindow? _overlayEditor;
    private OverlayEditorViewModel? _overlayEditorVm;

    private void ShowOverlayEditor()
    {
        if (_settings is null || _store is null) return;
        if (_overlayEditor is null)
        {
            _overlayEditorVm = new OverlayEditorViewModel(_settings, _store, SaveSettings);
            _overlayEditorVm.Applied += () =>
            {
                _overlayVm?.ApplyLayout();
                _overlayVm?.Rebuild();
                _dashboardVm?.SyncOverlaySelection();
                ApplyFrameTracing();
                PushOverlayStatus();
            };
            _overlayEditor = new OverlayEditorWindow { DataContext = _overlayEditorVm };
            _overlayEditor.Closed += (_, _) => { _overlayEditor = null; _overlayEditorVm = null; };
        }
        _overlayEditorVm?.Refresh();
        ShowMonitoringWindow(_overlayEditor);
    }

    private void ToggleFpsOnlyOverlay()
    {
        _overlayVm?.ToggleFpsOnly();
        PushOverlayStatus();
    }
}
