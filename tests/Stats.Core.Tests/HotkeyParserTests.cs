using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class HotkeyParserTests
{
    [Fact]
    public void Parse_CtrlShiftO()
    {
        var h = HotkeyParser.Parse("Ctrl+Shift+O");
        Assert.NotNull(h);
        Assert.Equal(HotkeyParser.ModCtrl | HotkeyParser.ModShift, h!.Modifiers);
        Assert.Equal(0x4Fu, h.VirtualKey); // 'O'
        Assert.Equal("Ctrl+Shift+O", h.Display);
    }

    [Fact]
    public void Parse_NormalizesCaseSpacingAndOrder()
    {
        var h = HotkeyParser.Parse(" alt + ctrl + s ");
        Assert.NotNull(h);
        Assert.Equal("Ctrl+Alt+S", h!.Display);
        Assert.Equal(0x53u, h.VirtualKey);
    }

    [Fact]
    public void Parse_FunctionKeyWithoutModifier_Allowed()
    {
        var h = HotkeyParser.Parse("F9");
        Assert.NotNull(h);
        Assert.Equal(0u, h!.Modifiers);
        Assert.Equal(0x78u, h.VirtualKey); // VK_F9
    }

    [Theory]
    [InlineData("O")]            // plain letter needs a modifier
    [InlineData("Ctrl+Foo")]     // unknown key
    [InlineData("Ctrl+Shift")]   // no key
    [InlineData("Ctrl+A+B")]     // two keys
    [InlineData("")]
    [InlineData(null)]
    public void Parse_Invalid_ReturnsNull(string? text) => Assert.Null(HotkeyParser.Parse(text));

    [Fact]
    public void Format_RoundTrips()
    {
        var h = HotkeyParser.Parse("Win+Shift+F2")!;
        Assert.Equal("Shift+Win+F2", h.Display);
        Assert.Equal(h.Display, HotkeyParser.Parse(h.Display)!.Display);
    }
}
