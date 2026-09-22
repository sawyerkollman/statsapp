using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Stats.App.Views;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.UiPreview.Tests;

public sealed class OverlayEditorKeyboardTests
{
    [Fact]
    public void FocusedCardNudge_TargetsOnlyTheFocusedCard_AndIgnoresEditorControls()
    {
        RunSta(() =>
        {
            var definitions = new[]
            {
                new MetricDefinition("a", "Card A", MetricGroup.Cpu, "CPU", "%"),
                new MetricDefinition("b", "Card B", MetricGroup.Gpu, "GPU", "%"),
            };
            var vm = new OverlayEditorViewModel(new AppSettings { OverlayMetrics = definitions.Select(definition => definition.Id).ToList() }, new MetricStore(definitions), () => { });
            var cardA = vm.Slots[0];
            var cardB = vm.Slots[1];
            vm.Move(cardB, 224, 24);
            var originalA = cardA.X;
            var originalB = cardB.X;

            Assert.True(TryGetArrowNudge(new Thumb { DataContext = cardB }, Key.Right, out var focusedSlot, out var dx, out var dy));
            vm.Nudge(focusedSlot, dx, dy);

            Assert.Equal(originalA, cardA.X);
            Assert.Equal(originalB + 8, cardB.X);
            Assert.False(TryGetArrowNudge(new TextBox(), Key.Right, out _, out _, out _));
        });
    }

    private static bool TryGetArrowNudge(IInputElement focusedElement, Key key, out OverlayCanvasSlot slot, out double dx, out double dy)
    {
        var method = typeof(OverlayEditorWindow).GetMethod("TryGetArrowNudge", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [focusedElement, key, null, 0d, 0d];
        var handled = Assert.IsType<bool>(method.Invoke(null, args));
        slot = handled ? Assert.IsType<OverlayCanvasSlot>(args[2]) : null!;
        dx = Assert.IsType<double>(args[3]);
        dy = Assert.IsType<double>(args[4]);
        return handled;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new TargetInvocationException(failure);
    }
}
