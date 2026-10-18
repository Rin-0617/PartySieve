using Newtonsoft.Json;

namespace PartySieve.Models;

public enum RuleAction
{
    /// <summary>条件に合う募集を隠す。</summary>
    Hide,

    /// <summary>条件に合わない募集を隠す（絞り込み）。</summary>
    RequireMatch,

    /// <summary>条件に合う募集は、他のルールやスパム判定に関係なく表示する。</summary>
    AlwaysShow,
}

public enum RuleTarget
{
    /// <summary>募集文・募集主・コンテンツ名をまとめて見る。</summary>
    All,
    Description,
    HostName,
    DutyName,
    DataCenter,
}

public enum ConditionJoiner
{
    And,
    Or,
}

public enum ConditionOperator
{
    Contains,
    NotContains,
    Regex,
}

/// <summary>
/// ルールの1行。「[対象] に [語] を [含む]」。
/// 2行目以降は前の行と「かつ」「または」でつなぐ。「かつ」が「または」より先にまとまる。
/// </summary>
[Serializable]
public sealed class RuleCondition
{
    public ConditionJoiner Joiner { get; set; } = ConditionJoiner.And;

    public RuleTarget Target { get; set; } = RuleTarget.Description;

    public ConditionOperator Operator { get; set; } = ConditionOperator.Contains;

    public string Text { get; set; } = string.Empty;

    public void Normalize()
    {
        this.Text ??= string.Empty;
        if (!Enum.IsDefined(this.Joiner))
        {
            this.Joiner = ConditionJoiner.And;
        }

        if (!Enum.IsDefined(this.Target))
        {
            this.Target = RuleTarget.Description;
        }

        if (!Enum.IsDefined(this.Operator))
        {
            this.Operator = ConditionOperator.Contains;
        }
    }
}

[Serializable]
public sealed class FilterRule
{
    public string Name { get; set; } = "新しいルール";

    public bool Enabled { get; set; } = true;

    public RuleAction Action { get; set; } = RuleAction.Hide;

    // Dalamud の設定読み込みは既定でリストに追記するため、置き換えを指定しないと読み込むたびに初期値の行が増える。
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<RuleCondition> Conditions { get; set; } = [new()];

    public void Normalize()
    {
        this.Name ??= string.Empty;
        if (!Enum.IsDefined(this.Action))
        {
            this.Action = RuleAction.Hide;
        }

        this.Conditions ??= [];
        if (this.Conditions.Count == 0)
        {
            this.Conditions.Add(new RuleCondition());
        }

        foreach (var condition in this.Conditions)
        {
            condition.Normalize();
        }
    }

    /// <summary>v2 までの初期ルールの名前。移行時に新しい初期ルールへ置き換える。</summary>
    public const string LegacyDefaultRuleName = "RMT・外部URL";

    public const string RmtRuleName = "RMT・外部サイト";

    public const string StaticRuleName = "固定募集（Discord・ロドストのURL）";

    /// <summary>ロドストの公式の短縮 URL（https://ffxiv.link/...）。</summary>
    public const string LodestoneShortLink = "ffxiv.link";

    private const string LodestoneHost = "finalfantasyxiv.com";

    public static List<FilterRule> CreateDefault() =>
    [
        new()
        {
            // Discord とロドストの URL は固定募集でよく使われるので、ここでは隠さない（下のルールで扱う）。
            // 短縮 URL は行き先が見えないので、RMT と同じ扱いで隠す。
            Name = RmtRuleName,
            Action = RuleAction.Hide,
            Conditions =
            [
                Contains("RMT"),
                Contains("http", ConditionJoiner.Or),
                NotContains("discord"),
                NotContains(LodestoneHost),
                NotContains(LodestoneShortLink),
                Contains("www.", ConditionJoiner.Or),
                NotContains("discord"),
                NotContains(LodestoneHost),
                NotContains(LodestoneShortLink),
                new()
                {
                    Joiner = ConditionJoiner.Or,
                    Operator = ConditionOperator.Regex,
                    Text = @"\d+\s*(m|万)\s*gil",
                },
            ],
        },
        new()
        {
            // 固定を探している人はこのルールを無効にする。
            // 「VCはDiscord」のような野良募集を巻き込まないよう、URL の形だけを対象にする。
            Name = StaticRuleName,
            Action = RuleAction.Hide,
            Conditions =
            [
                Contains("discord.gg"),
                Contains("discord.com", ConditionJoiner.Or),
                Contains(LodestoneHost, ConditionJoiner.Or),
                Contains(LodestoneShortLink, ConditionJoiner.Or),
            ],
        },
    ];

    /// <summary>
    /// v4 までの初期ルールに、ロドストの短縮 URL（ffxiv.link）の行を足す。
    /// 初期ルールを書き換えている場合も、ロドストの URL を扱っている行の隣に足すだけで、他の行には触れない。
    /// </summary>
    public static void AddLodestoneShortLink(List<FilterRule> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.Conditions.Exists(c => c.Text.Contains(LodestoneShortLink, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (rule.Name == RmtRuleName)
            {
                for (var i = rule.Conditions.Count - 1; i >= 0; i--)
                {
                    var condition = rule.Conditions[i];
                    if (condition.Operator == ConditionOperator.NotContains && condition.Text == LodestoneHost)
                    {
                        rule.Conditions.Insert(i + 1, NotContains(LodestoneShortLink));
                    }
                }
            }
            else if (rule.Name == StaticRuleName)
            {
                rule.Conditions.Add(Contains(LodestoneShortLink, ConditionJoiner.Or));
            }
        }
    }

    private static RuleCondition Contains(string text, ConditionJoiner joiner = ConditionJoiner.And) =>
        new() { Joiner = joiner, Text = text };

    private static RuleCondition NotContains(string text) =>
        new() { Operator = ConditionOperator.NotContains, Text = text };
}
