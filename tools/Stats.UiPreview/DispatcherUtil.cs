using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Stats.UiPreview;

/// <summary>The preview never calls Application.Run(), so nothing pumps the STA thread's message loop on its own
/// — these helpers push/drain one dispatcher frame at a time instead, standing in for "a settled dispatcher/render
/// cycle" (PREVIEW_HARNESS.md's capture-behavior section).</summary>
internal static class DispatcherUtil
{
    /// <summary>Drains the queue down to (and including) one Background-priority item — enough for layout/bindings
    /// queued so far to run, without blocking forever on a steady stream of new work.</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public static void WaitFrames(int count = 5)
    {
        for (int i = 0; i < count; i++) DoEvents();
    }

    /// <summary>Waits for the window's first ContentRendered, then a few more idle passes plus one
    /// CompositionTarget.Rendering tick so layout/render has actually settled before a screenshot.</summary>
    public static void WaitForSettled(Window window, TimeSpan timeout)
    {
        bool rendered = window.IsLoaded && window.IsVisible;
        void OnRendered(object? s, EventArgs e) => rendered = true;
        window.ContentRendered += OnRendered;
        var sw = Stopwatch.StartNew();
        while (!rendered && sw.Elapsed < timeout) DoEvents();
        window.ContentRendered -= OnRendered;

        bool frameRendered = false;
        void OnFrame(object? s, EventArgs e) => frameRendered = true;
        CompositionTarget.Rendering += OnFrame;
        sw.Restart();
        while (!frameRendered && sw.Elapsed < timeout) DoEvents();
        CompositionTarget.Rendering -= OnFrame;

        WaitFrames(8);
    }
}
