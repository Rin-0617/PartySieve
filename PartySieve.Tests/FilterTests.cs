using PartySieve.Filtering;
using PartySieve.Models;
using Xunit;

namespace PartySieve.Tests;

public class FilterTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static ListingInfo Listing(ulong host, string description, string duty = "絶もうひとつの未来") =>
        new() { Id = host, ContentId = host, Description = description, DutyName = duty };

    [Fact]
    public void DuplicateTextFromManyHostsIsHidden()
    {
        var filter = new ListingFilter();
        var spam = new SpamSettings { DuplicateHostThreshold = 3 };
        const string text = "格安で装備を用意しますお気軽にどうぞ";

        for (ulong host = 1; host <= 2; host++)
        {
            filter.Spam.Observe(Listing(host, text), spam, Now);
        }

        Assert.False(filter.Evaluate(Listing(2, text), [], spam, Now).Hidden);

        // 記号や空白を変えただけの3人目で、しきい値に届く。
        var third = Listing(3, "格安で装備を 用意します！お気軽にどうぞ");
        filter.Spam.Observe(third, spam, Now);
        var result = filter.Evaluate(third, [], spam, Now);
        Assert.True(result.Hidden);
        Assert.Contains(result.Reasons, r => r.Contains("3人"));
    }

    [Fact]
    public void DuplicateWindowExpires()
    {
        var filter = new ListingFilter();
        var spam = new SpamSettings { DuplicateHostThreshold = 2, DuplicateWindowMinutes = 10 };
        const string text = "同じ文面の募集がたくさん出ています";

        filter.Spam.Observe(Listing(1, text), spam, Now.AddMinutes(-30));
        filter.Spam.Observe(Listing(2, text), spam, Now);
        Assert.False(filter.Evaluate(Listing(2, text), [], spam, Now).Hidden);
    }

    [Fact]
    public void ShortCommonTextIsNotDuplicate()
    {
        var filter = new ListingFilter();
        var spam = new SpamSettings { DuplicateHostThreshold = 2 };
        for (ulong host = 1; host <= 5; host++)
        {
            filter.Spam.Observe(Listing(host, "よろしく"), spam, Now);
        }

        Assert.False(filter.Evaluate(Listing(1, "よろしく"), [], spam, Now).Hidden);
    }

    [Theory]
    [InlineData("★☆★☆★☆★☆★☆★☆★☆", true)]
    [InlineData("クリア目的 22時〜24時 ゴリラ式", false)]
    [InlineData("あああああああああああ", true)]
    [InlineData("練習 ~~~ よろしく", false)]
    public void SymbolAndRepeat(string description, bool hidden)
    {
        var filter = new ListingFilter();
        var spam = new SpamSettings();
        Assert.Equal(hidden, filter.Evaluate(Listing(1, description), [], spam, Now).Hidden);
    }

    private static RuleCondition Row(
        string text,
        ConditionJoiner joiner = ConditionJoiner.And,
        RuleTarget target = RuleTarget.Description,
        ConditionOperator op = ConditionOperator.Contains) =>
        new() { Joiner = joiner, Target = target, Operator = op, Text = text };

    private static FilterRule Rule(string name, RuleAction action, params RuleCondition[] rows) =>
        new() { Name = name, Action = action, Conditions = [.. rows] };

    [Fact]
    public void RulesHideRequireAndAlwaysShow()
    {
        var filter = new ListingFilter();
        var spam = new SpamSettings();
        var rules = new List<FilterRule>
        {
            // 絶では「クリア目的」以外を隠す。
            Rule(
                "絶はクリア目的のみ",
                RuleAction.Hide,
                Row("絶", target: RuleTarget.DutyName),
                Row("クリア目的", op: ConditionOperator.NotContains)),
            Rule("練習除外", RuleAction.Hide, Row("練習")),
        };

        Assert.False(filter.Evaluate(Listing(1, "クリア目的"), rules, spam, Now).Hidden);
        Assert.True(filter.Evaluate(Listing(2, "消化"), rules, spam, Now).Hidden);
        Assert.False(filter.Evaluate(Listing(3, "消化", "零式"), rules, spam, Now).Hidden);

        var hidden = filter.Evaluate(Listing(4, "クリア目的 練習"), rules, spam, Now);
        Assert.True(hidden.Hidden);
        Assert.Single(hidden.Reasons);

        rules.Insert(0, Rule("固定メンバー", RuleAction.AlwaysShow, Row("固定")));
        var shown = filter.Evaluate(Listing(5, "固定 練習 ★★★★★★★★★★★★"), rules, spam, Now);
        Assert.False(shown.Hidden);
        Assert.Equal("固定メンバー", shown.ShownBy);
    }

    [Fact]
    public void RequireMatchKeepsOnlyMatches()
    {
        var filter = new ListingFilter();
        var rules = new List<FilterRule>
        {
            Rule("クリアか消化", RuleAction.RequireMatch, Row("クリア"), Row("消化", ConditionJoiner.Or)),
        };

        Assert.False(filter.Evaluate(Listing(1, "クリア目的"), rules, new SpamSettings(), Now).Hidden);
        Assert.False(filter.Evaluate(Listing(2, "週制限消化"), rules, new SpamSettings(), Now).Hidden);
        Assert.True(filter.Evaluate(Listing(3, "練習"), rules, new SpamSettings(), Now).Hidden);
    }

    [Theory]
    [InlineData("A B", true)]
    [InlineData("A", false)]
    [InlineData("C", true)]
    [InlineData("B C", true)]
    [InlineData("B", false)]
    public void AndBindsTighterThanOr(string description, bool expected)
    {
        // A かつ B または C  =  (A ∧ B) ∨ C
        var rule = Rule("r", RuleAction.Hide, Row("A"), Row("B"), Row("C", ConditionJoiner.Or));
        Assert.Equal(expected, new ListingFilter().Matches(rule, Listing(1, description)));
    }

    [Fact]
    public void EmptyRowsAreIgnored()
    {
        var filter = new ListingFilter();
        Assert.Null(filter.Matches(Rule("r", RuleAction.Hide, Row(" ")), Listing(1, "何でも")));
        Assert.True(filter.Matches(Rule("r", RuleAction.Hide, Row("練習"), Row("")), Listing(1, "練習")));
    }

    [Fact]
    public void ContainsIgnoresWidthCaseAndKana()
    {
        var rule = Rule("r", RuleAction.Hide, Row("ゴリラ"), Row("ＲＭＴ", ConditionJoiner.Or));
        var filter = new ListingFilter();
        Assert.True(filter.Matches(rule, Listing(1, "散開はごりら式")));
        Assert.True(filter.Matches(rule, Listing(2, "rmt")));
    }

    [Theory]
    [InlineData("安い！ https://example.xyz", "RMT・外部サイト")]
    [InlineData("詳細は bit.ly/abc か http://bit.ly/abc", "RMT・外部サイト")]
    [InlineData("100万gil 即納", "RMT・外部サイト")]
    [InlineData("固定募集 https://discord.gg/abcd", "固定募集（Discord・ロドストのURL）")]
    [InlineData("詳細 jp.finalfantasyxiv.com/lodestone/pt/xxx", "固定募集（Discord・ロドストのURL）")]
    [InlineData("にて！→https://ffxiv.link/0145057", "固定募集（Discord・ロドストのURL）")]
    [InlineData("クリア目的 マクロはぬけまる", null)]
    [InlineData("VCはDiscord使用 聞き専可", null)]
    public void DefaultRules(string description, string? hiddenBy)
    {
        var filter = new ListingFilter();
        var rules = FilterRule.CreateDefault();
        var result = filter.Evaluate(Listing(1, description), rules, new SpamSettings(), Now);
        if (hiddenBy is null)
        {
            Assert.False(result.Hidden);
            return;
        }

        Assert.Equal([$"ルール「{hiddenBy}」"], result.Reasons);
    }

    [Fact]
    public void DisablingStaticRuleShowsLodestoneShortLinks()
    {
        var filter = new ListingFilter();
        var rules = FilterRule.CreateDefault();
        rules[1].Enabled = false;
        Assert.False(filter.Evaluate(Listing(1, "固定募集 https://ffxiv.link/0145057"), rules, new SpamSettings(), Now).Hidden);
    }

    [Fact]
    public void MigrationAddsLodestoneShortLinkToOldDefaults()
    {
        // v4 までの初期ルール（ffxiv.link の行がない）。
        var rules = FilterRule.CreateDefault();
        foreach (var rule in rules)
        {
            rule.Conditions.RemoveAll(c => c.Text == FilterRule.LodestoneShortLink);
        }

        var expected = FilterRule.CreateDefault().Select(r => r.Conditions.Count).ToList();
        FilterRule.AddLodestoneShortLink(rules);
        FilterRule.AddLodestoneShortLink(rules);
        Assert.Equal(expected, rules.Select(r => r.Conditions.Count).ToList());

        var filter = new ListingFilter();
        var result = filter.Evaluate(Listing(1, "固定 https://ffxiv.link/0145057"), rules, new SpamSettings(), Now);
        Assert.Equal(["ルール「固定募集（Discord・ロドストのURL）」"], result.Reasons);

        rules[1].Enabled = false;
        Assert.False(filter.Evaluate(Listing(2, "固定 https://ffxiv.link/0145057"), rules, new SpamSettings(), Now).Hidden);
    }

    [Fact]
    public void DisablingStaticRuleShowsDiscordListings()
    {
        var filter = new ListingFilter();
        var rules = FilterRule.CreateDefault();
        rules[1].Enabled = false;
        Assert.False(filter.Evaluate(Listing(1, "固定募集 https://discord.gg/abcd"), rules, new SpamSettings(), Now).Hidden);
    }

    [Fact]
    public void InvalidRegexRowIsSkipped()
    {
        var filter = new ListingFilter();
        var rules = new List<FilterRule> { Rule("r", RuleAction.Hide, Row("([", op: ConditionOperator.Regex)) };
        Assert.False(filter.Evaluate(Listing(1, "(["), rules, new SpamSettings(), Now).Hidden);
        Assert.NotNull(filter.CompileRegex("([").Error);
    }
}

public class DataCenterTests
{
    [Theory]
    [InlineData("Tonberry", "Elemental")]
    [InlineData("Ridill", "Gaia")]
    [InlineData("anima", "Mana")]
    [InlineData("Zeromus", "Meteor")]
    [InlineData("Excalibur", null)]
    public void WorldMapsToLogicalDataCenter(string world, string? expected)
    {
        Assert.Equal(expected, PartySieve.Services.DataCenterPalette.DataCenterOfWorld(world));
    }
}

public class DataCenterExclusionTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static ListingInfo Host(string dc) => new() { Id = 1, ContentId = 1, HomeDataCenter = dc, Description = "クリア目的" };

    [Fact]
    public void ExcludedDataCenterIsHidden()
    {
        var filter = new ListingFilter();
        string[] excluded = ["Elemental"];
        Assert.True(filter.Evaluate(Host("Elemental"), [], new SpamSettings(), Now, excluded).Hidden);
        Assert.False(filter.Evaluate(Host("Gaia"), [], new SpamSettings(), Now, excluded).Hidden);
        Assert.False(filter.Evaluate(Host(""), [], new SpamSettings(), Now, excluded).Hidden);
    }

    [Fact]
    public void AlwaysShowOverridesDataCenterExclusion()
    {
        var filter = new ListingFilter();
        var rules = new List<FilterRule>
        {
            new() { Name = "固定", Action = RuleAction.AlwaysShow, Conditions = [new() { Text = "クリア目的" }] },
        };
        Assert.False(filter.Evaluate(Host("Elemental"), rules, new SpamSettings(), Now, ["Elemental"]).Hidden);
    }

    [Fact]
    public void DataCenterCanBeARuleCondition()
    {
        var rule = new FilterRule
        {
            Conditions = [new() { Target = RuleTarget.DataCenter, Text = "Elemental" }],
        };
        Assert.True(new ListingFilter().Matches(rule, Host("Elemental")));
        Assert.False(new ListingFilter().Matches(rule, Host("Mana")));
    }
}
