using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;
using PartySieve.Models;

namespace PartySieve;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    private const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>false にすると判定だけして、募集は隠さない（消しすぎの確認用）。</summary>
    public bool HideMatches { get; set; } = true;

    /// <summary>初めて見てからこの分数のあいだは「新着」として強調する。</summary>
    public int NewHighlightMinutes { get; set; } = 5;

    /// <summary>公式のパーティ募集一覧で、募集主の名前をホームDCの色で塗る。</summary>
    public bool ColorHostByDataCenter { get; set; } = true;

    /// <summary>公式の詳細検索の横に、高機能フィルターのパネルを出す。</summary>
    public bool ShowSearchPanel { get; set; } = true;

    /// <summary>募集を表示しない、募集主のホームDC。</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> ExcludedDataCenters { get; set; } = [];

    public SpamSettings Spam { get; set; } = new();

    // Dalamud の設定読み込みは既定でリストに追記するため、置き換えを指定しないと読み込むたびに初期ルールが増える。
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<FilterRule> Rules { get; set; } = FilterRule.CreateDefault();

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;

        if (this.Version < CurrentVersion)
        {
            this.Migrate();
            this.Version = CurrentVersion;
            this.Save();
            return;
        }

        this.Normalize();
    }

    private void Migrate()
    {
        // v1 のルールは検索式だったので、行形式の初期ルールに置き換える。
        if (this.Version < 2)
        {
            this.Rules = FilterRule.CreateDefault();
            return;
        }

        this.Rules ??= [];

        // v2 の初期ルール「RMT・外部URL」は Discord も隠していたので、新しい2つの初期ルールに置き換える。
        // 自分で作ったルールは残す。
        if (this.Version < 3)
        {
            var index = this.Rules.FindIndex(r => r.Name == FilterRule.LegacyDefaultRuleName);
            if (index >= 0)
            {
                this.Rules.RemoveAt(index);
                this.Rules.InsertRange(index, FilterRule.CreateDefault());
            }
        }

        // v3 までは読み込むたびに初期ルールと空の条件行が追記されていたので、増えた分を取り除く。
        if (this.Version < 4)
        {
            RemoveDuplicatedDefaults(this.Rules);
            foreach (var rule in this.Rules)
            {
                rule.Conditions ??= [];
                rule.Conditions.RemoveAll(c => c is null);
                while (rule.Conditions.Count > 1 && rule.Conditions.FindIndex(c => string.IsNullOrWhiteSpace(c.Text)) is var empty and >= 0)
                {
                    rule.Conditions.RemoveAt(empty);
                }
            }
        }
    }

    /// <summary>初期ルールと同じ名前のルールが複数あれば、最初の1つだけ残す。</summary>
    private static void RemoveDuplicatedDefaults(List<FilterRule> rules)
    {
        var defaultNames = FilterRule.CreateDefault().Select(r => r.Name).Append(FilterRule.LegacyDefaultRuleName).ToHashSet();
        var seen = new HashSet<string>();
        rules.RemoveAll(r => r is null || (defaultNames.Contains(r.Name) && !seen.Add(r.Name)));
    }

    public void Normalize()
    {
        this.NewHighlightMinutes = Math.Clamp(this.NewHighlightMinutes, 0, 120);
        this.ExcludedDataCenters ??= [];
        this.Spam ??= new SpamSettings();
        this.Spam.Normalize();
        this.Rules ??= FilterRule.CreateDefault();
        foreach (var rule in this.Rules)
        {
            rule.Normalize();
        }
    }

    public void Save()
    {
        this.Normalize();
        this.pluginInterface?.SavePluginConfig(this);
    }
}
