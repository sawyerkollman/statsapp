using Stats.Core.Lab;
using Stats.Core.Settings;

namespace Stats.Core.Tests;
public sealed class CommandCentreLabTests
{
    [Fact] public void Notebook_RoundTripsAndFailedLoadCannotOverwrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Stats.Notebook", Guid.NewGuid().ToString("N"));
        try
        {
            var notebook = new TuningNotebook { Entries = [new() { Name = "Baseline", StabilityNotes = "Two repeat runs" }] };
            TuningNotebook.Save(directory, notebook);
            Assert.Equal("Two repeat runs", Assert.Single(TuningNotebook.Load(directory).Entries).StabilityNotes);
            var path = Path.Combine(directory, "tuning-notebook.json");
            var saved = File.ReadAllText(path);
            notebook.Entries[0].Workload = new string('x', 1001);
            Assert.Throws<InvalidDataException>(() => TuningNotebook.Save(directory, notebook));
            Assert.Equal(saved, File.ReadAllText(path));
            File.WriteAllText(path, "broken json");
            using var vm = new Stats.Core.ViewModels.LabViewModel(directory);
            vm.AddNotebookEntryCommand.Execute(null); vm.SaveNotebookCommand.Execute(null);
            Assert.Equal("broken json", File.ReadAllText(path));
            Assert.Contains("refusing", vm.Error);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public void NamedRulesUseTheSameValidationAsActiveRules()
    {
        var id = Guid.NewGuid().ToString("N");
        var options = LabOptionsStore.Sanitize(new() { NamedCompoundRuleSets = [new() { Name = "Game", Rules = [
            new() { Id = id, FirstThreshold = float.NaN, Enabled = true, HoldSeconds = -1 },
            new() { Id = id, FirstMetricId = null!, CooldownSeconds = 99999 }] }] });
        var rules = options.NamedCompoundRuleSets[0].Rules;
        Assert.False(rules[0].Enabled); Assert.True(rules[0].TestOnly); Assert.Equal(1, rules[0].HoldSeconds);
        Assert.NotEqual(rules[0].Id, rules[1].Id); Assert.Equal("", rules[1].FirstMetricId); Assert.Equal(3600, rules[1].CooldownSeconds);
    }
    [Fact] public void Appearance_OnlyChangesThemeFields()
    {
        var settings = new AppSettings { FanControlEnabled = true, CheckForUpdatesAutomatically = false };
        Assert.True(GameAppearance.Apply(settings, "Light")); Assert.Equal("Light", settings.ThemePreset); Assert.True(settings.FanControlEnabled); Assert.False(settings.CheckForUpdatesAutomatically);
    }
    [Fact] public void Notebook_RejectsNonSessionLinks()
    {
        Assert.Throws<InvalidDataException>(() => TuningNotebook.Normalize(new TuningNotebook { Entries = [new() { RecordingA = "x.txt" }] }));
    }
    [Fact] public void Options_ClampsNamedRuleSets()
    {
        var options = LabOptionsStore.Sanitize(new LabOptions { NamedCompoundRuleSets = Enumerable.Range(0, 20).Select(i => new NamedCompoundRuleSet { Name = i.ToString(), Rules = Enumerable.Range(0, 40).Select(_ => new CompoundRule()).ToList() }).ToList() });
        Assert.Equal(16, options.NamedCompoundRuleSets.Count); Assert.All(options.NamedCompoundRuleSets, x => Assert.Equal(32, x.Rules.Count));
    }
}
