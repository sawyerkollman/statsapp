using System.Reflection;
using System.Windows.Input;
using Stats.App.Views;

namespace Stats.UiPreview.Tests;

public class DashboardKeyboardTests
{
    [Theory]
    [InlineData(ModifierKeys.None, true)]
    [InlineData(ModifierKeys.Shift, true)]
    [InlineData(ModifierKeys.Control, false)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(ModifierKeys.Alt, false)]
    [InlineData(ModifierKeys.Windows, false)]
    public void ArrowNudge_OnlyAcceptsUnmodifiedOrShift(ModifierKeys modifiers, bool expected)
    {
        var method = typeof(DashboardWindow).GetMethod("TryGetArrowDelta", BindingFlags.Static | BindingFlags.NonPublic)!;
        object[] args = [Key.Right, modifiers, 0d, 0d];
        Assert.Equal(expected, method.Invoke(null, args));
        Assert.Equal(expected ? 1d : 0d, args[2]);
        Assert.Equal(0d, args[3]);
    }
}
