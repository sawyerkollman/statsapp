using Stats.Core.Updates;

namespace Stats.Core.Tests;

public class HelperCmdScriptTests
{
    [Fact]
    public void BuildUpdateScript_SetsUtf8CodePage_AndEscapesPercent()
    {
        var script = HelperCmdScript.BuildUpdateScript(42, @"C:\Users\100%完了\Setup.exe", @"D:\Stats\Stats.App.exe");

        Assert.StartsWith("@echo off\r\nchcp 65001 >nul\r\nsetlocal DisableDelayedExpansion\r\n", script);
        Assert.Contains("\"C:\\Users\\100%%完了\\Setup.exe\" /SILENT /NOCANCEL\r\n", script);
        Assert.Contains("start \"\" \"D:\\Stats\\Stats.App.exe\"\r\n", script);
        Assert.Contains("%n%", script);
        Assert.DoesNotContain("100%完了", script);
    }

    [Fact]
    public void BuildRestartScript_QuotesExe_AndDoesNotRunInstaller()
    {
        var script = HelperCmdScript.BuildRestartScript(7, @"C:\Program Files\Stats\Stats.App.exe");

        Assert.Contains("chcp 65001", script);
        Assert.Contains("start \"\" \"C:\\Program Files\\Stats\\Stats.App.exe\"\r\n", script);
        Assert.DoesNotContain("/SILENT", script);
    }

    [Fact]
    public void GetBytes_StartsWithUtf8Bom()
    {
        var bytes = HelperCmdScript.GetBytes("ok");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'o', (byte)'k' }, bytes);
    }

    [Theory]
    [InlineData("C:\\a\"b.exe")]
    [InlineData("C:\\a\r\nb.exe")]
    public void BuildRestartScript_RejectsQuotesAndNewlines(string path)
    {
        Assert.Throws<ArgumentException>(() => HelperCmdScript.BuildRestartScript(1, path));
    }
}
