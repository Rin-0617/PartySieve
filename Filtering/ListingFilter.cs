using System.Text.RegularExpressions;
using PartySieve.Models;

namespace PartySieve.Filtering;

public sealed record FilterResult(bool Hidden, IReadOnlyList<string> Reasons, string? ShownBy)
{
    public static readonly FilterResult Visible = new(false, [], null);
}

/// <summary>
/// ルールとスパム判定を合わせて、募集を隠すかどうかを決める。
/// </summary>
public sealed class ListingFilter
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private readonly Dictionary<string, (Query? Query, string? Error)> compiledQueries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Regex? Regex, string? Error)> compiledRegexes = new(StringComparer.Ordinal);

    public SpamDetector Spam { get; } = new();

    /// <summary>検索式（受信ログの検索欄用）を解析する。結果はキャッシュする。</summary>
    public (Query? Query, string? Error) Compile(string pattern)
    {
        if (!this.compiledQueries.TryGetValue(pattern, out var entry))
        {
            Query.TryParse(pattern, out var query, out var error);
            entry = (query, error);
            Remember(this.compiledQueries, pattern, entry);
        }

        return entry;
    }

    /// <summary>正規表現の行を解析する。不正ならエラーメッセージを返す。</summary>
    public (Regex? Regex, string? Error) CompileRegex(string pattern)
    {
        if (!this.compiledRegexes.TryGetValue(pattern, out var entry))
        {
            try
            {
                entry = (new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout), null);
            }
            catch (ArgumentException ex)
            {
                entry = (null, $"正規表現が不正です: {ex.Message}");
            }

            Remember(this.compiledRegexes, pattern, entry);
        }

        return entry;
    }

    /// <summary>
    /// ルールの条件に合うか。空の行と不正な正規表現の行は無視し、有効な行がひとつもなければ null。
    /// 「かつ」でつながった行をまとめ、そのまとまりのどれかが成り立てば一致とする。
    /// </summary>
    public bool? Matches(FilterRule rule, ListingInfo listing)
    {
        bool? result = null;
        bool? group = null;
        foreach (var condition in rule.Conditions)
        {
            if (condition.Joiner == ConditionJoiner.Or && group is not null)
            {
                result = (result ?? false) || group.Value;
                group = null;
            }

            if (this.MatchCondition(condition, listing) is not { } matched)
            {
                continue;
            }

            group = (group ?? true) && matched;
        }

        if (group is not null)
        {
            result = (result ?? false) || group.Value;
        }

        return result;
    }

    private bool? MatchCondition(RuleCondition condition, ListingInfo listing)
    {
        if (string.IsNullOrWhiteSpace(condition.Text))
        {
            return null;
        }

        var text = TargetText(listing, condition.Target);
        switch (condition.Operator)
        {
            case ConditionOperator.Regex:
            {
                var (regex, _) = this.CompileRegex(condition.Text);
                if (regex is null)
                {
                    return null;
                }

                try
                {
                    return regex.IsMatch(text) || regex.IsMatch(TextNormalizer.Normalize(text));
                }
                catch (RegexMatchTimeoutException)
                {
                    return null;
                }
            }

            case ConditionOperator.NotContains:
                return !Contains(text, condition.Text);
            default:
                return Contains(text, condition.Text);
        }
    }

    private static bool Contains(string text, string word) =>
        TextNormalizer.Normalize(text).Contains(TextNormalizer.Normalize(word.Trim()), StringComparison.Ordinal);

    public FilterResult Evaluate(
        ListingInfo listing,
        IReadOnlyList<FilterRule> rules,
        SpamSettings spam,
        DateTime now,
        IReadOnlyCollection<string>? excludedDataCenters = null)
    {
        var reasons = new List<string>();
        foreach (var rule in rules)
        {
            if (!rule.Enabled || this.Matches(rule, listing) is not { } matched)
            {
                continue;
            }

            switch (rule.Action)
            {
                case RuleAction.AlwaysShow when matched:
                    return new FilterResult(false, [], rule.Name);
                case RuleAction.Hide when matched:
                    reasons.Add($"ルール「{rule.Name}」");
                    break;
                case RuleAction.RequireMatch when !matched:
                    reasons.Add($"ルール「{rule.Name}」に合わない");
                    break;
            }
        }

        if (excludedDataCenters is not null && listing.HomeDataCenter.Length > 0
            && excludedDataCenters.Contains(listing.HomeDataCenter, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add($"表示しないDC（{listing.HomeDataCenter}）");
        }

        reasons.AddRange(this.Spam.Detect(listing, spam, now));
        return reasons.Count == 0 ? FilterResult.Visible : new FilterResult(true, reasons, null);
    }

    public static string TargetText(ListingInfo listing, RuleTarget target) => target switch
    {
        RuleTarget.Description => listing.Description,
        RuleTarget.HostName => listing.HostName,
        RuleTarget.DutyName => listing.DutyName,
        RuleTarget.DataCenter => listing.HomeDataCenter,
        _ => $"{listing.DutyName}\n{listing.HostName}\n{listing.Description}",
    };

    private static void Remember<T>(Dictionary<string, T> cache, string key, T value)
    {
        if (cache.Count > 256)
        {
            cache.Clear();
        }

        cache[key] = value;
    }
}
