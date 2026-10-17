using Newtonsoft.Json;
using PartySieve.Models;
using Xunit;

namespace PartySieve.Tests;

public class SerializationTests
{
    // Dalamud の PluginConfigurations と同じ設定（保存は型名つき、読み込みは既定の追記モード）。
    private static readonly JsonSerializerSettings SaveSettings = new()
    {
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        TypeNameHandling = TypeNameHandling.Objects,
    };

    [Fact]
    public void ReloadingDoesNotDuplicateConditions()
    {
        var rules = FilterRule.CreateDefault();
        var expected = rules.Select(r => r.Conditions.Count).ToList();

        for (var i = 0; i < 3; i++)
        {
            var json = JsonConvert.SerializeObject(rules, Formatting.Indented, SaveSettings);
            rules = JsonConvert.DeserializeObject<List<FilterRule>>(json, new JsonSerializerSettings())!;
        }

        Assert.Equal(expected, rules.Select(r => r.Conditions.Count).ToList());
        Assert.Equal("RMT", rules[0].Conditions[0].Text);
    }
}
