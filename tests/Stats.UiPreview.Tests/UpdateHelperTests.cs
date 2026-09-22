using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Stats.UiPreview.Tests;

public class UpdateHelperTests
{
    [Theory]
    [InlineData(@"C:\Stats\Stats.App.exe", @"c:\stats\stats.app.exe", true)]
    [InlineData(@"C:\Other\Stats.App.exe", @"C:\Stats\Stats.App.exe", false)]
    [InlineData(@"C:\Stats\Stats.App.exe", null, false)]
    public void SecondaryInstanceRequiresTheSameExecutable(string candidate, string? current, bool expected)
    {
        var type = typeof(Stats.App.App).Assembly.GetType("Stats.App.Helpers.SingleInstance")!;
        Assert.Equal(expected, type.GetMethod("IsCurrentExecutable", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [candidate, current]));
    }

    [Fact]
    public void HardwareOwnershipIsGlobalWithOnlyAdministratorsAndSystemAccess()
    {
        // Inspect the descriptor only; never create the production mutex or start App.
        var type = typeof(Stats.App.App).Assembly.GetType("Stats.App.Helpers.SingleInstance")!;
        Assert.Equal(@"Global\Stats.Native.v1", type.GetField("MutexName", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue());
        var security = Assert.IsType<MutexSecurity>(type.GetMethod("CreateSecurity", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null));
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), security.GetOwner(typeof(SecurityIdentifier)));
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<MutexAccessRule>().ToArray();
        Assert.Equal(2, rules.Length);
        Assert.All(rules, rule =>
        {
            Assert.Equal(MutexRights.FullControl, rule.MutexRights);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        });
        Assert.Contains(rules, rule => rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
        Assert.Contains(rules, rule => rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
    }

    [Fact]
    public void HelperCapturesInstallerExitBeforeRelaunchAndPreservesExpectedVersion()
    {
        var method = typeof(Stats.App.App).GetMethod("BuildUpdateScript", BindingFlags.Static | BindingFlags.NonPublic)!;
        var script = Assert.IsType<string>(method.Invoke(null, [123, @"C:\secure\setup.exe", @"C:\Program Files\Stats\Stats.App.exe", "v1.11.0-beta.7"]));
        Assert.Contains("/NORESTART /RESTARTEXITCODE=3010", script);
        Assert.Contains("set installResult=%errorlevel%\r\nstart", script);
        Assert.Contains("--update-exit-code %installResult% --update-expected \"v1.11.0-beta.7\"", script);
        Assert.DoesNotContain("taskkill", script);
    }
}
