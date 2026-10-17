using System.Globalization;
using PartySieve.Models;

namespace PartySieve.Filtering;

/// <summary>
/// 検索妨害のような募集を見つける。重複判定のために、最近見た文面と募集主を覚えておく。
/// </summary>
public sealed class SpamDetector
{
    // 文面キー → (募集主 ContentId → 最後に見た時刻)
    private readonly Dictionary<string, Dictionary<ulong, DateTime>> recentHosts = new(StringComparer.Ordinal);

    /// <summary>募集を見たことを記録する。判定の前に呼ぶ。</summary>
    public void Observe(ListingInfo listing, SpamSettings settings, DateTime now)
    {
        var key = TextNormalizer.DuplicateKey(listing.Description);
        if (key.Length < settings.DuplicateMinLength)
        {
            return;
        }

        if (!this.recentHosts.TryGetValue(key, out var hosts))
        {
            hosts = [];
            this.recentHosts[key] = hosts;
        }

        hosts[listing.ContentId] = now;
    }

    public void Prune(SpamSettings settings, DateTime now)
    {
        var cutoff = now - TimeSpan.FromMinutes(settings.DuplicateWindowMinutes);
        foreach (var (key, hosts) in this.recentHosts.ToList())
        {
            foreach (var (host, seen) in hosts.ToList())
            {
                if (seen < cutoff)
                {
                    hosts.Remove(host);
                }
            }

            if (hosts.Count == 0)
            {
                this.recentHosts.Remove(key);
            }
        }
    }

    public void Clear() => this.recentHosts.Clear();

    /// <summary>スパムと判断した理由を返す。問題なければ空。</summary>
    public List<string> Detect(ListingInfo listing, SpamSettings settings, DateTime now)
    {
        var reasons = new List<string>();
        var description = listing.Description;

        if (settings.DuplicateEnabled)
        {
            var count = this.CountRecentHosts(description, settings, now);
            if (count >= settings.DuplicateHostThreshold)
            {
                reasons.Add($"同じ文面を{count}人が投稿");
            }
        }

        if (settings.SymbolEnabled && SymbolRatio(description, settings.SymbolMinLength) is { } ratio
            && ratio >= settings.SymbolRatio)
        {
            reasons.Add($"記号が多い（{ratio:P0}）");
        }

        if (settings.RepeatEnabled && LongestRun(description) is var run && run >= settings.RepeatRunLength)
        {
            reasons.Add($"同じ文字が{run}回連続");
        }

        return reasons;
    }

    private int CountRecentHosts(string description, SpamSettings settings, DateTime now)
    {
        var key = TextNormalizer.DuplicateKey(description);
        if (key.Length < settings.DuplicateMinLength || !this.recentHosts.TryGetValue(key, out var hosts))
        {
            return 0;
        }

        var cutoff = now - TimeSpan.FromMinutes(settings.DuplicateWindowMinutes);
        return hosts.Values.Count(seen => seen >= cutoff);
    }

    /// <summary>空白以外の文字のうち、文字でも数字でもないものの割合。短すぎる文は null。</summary>
    public static float? SymbolRatio(string text, int minLength)
    {
        var total = 0;
        var symbols = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            if (string.IsNullOrWhiteSpace(element))
            {
                continue;
            }

            total++;
            if (!char.IsLetterOrDigit(element, 0))
            {
                symbols++;
            }
        }

        return total < minLength ? null : (float)symbols / total;
    }

    /// <summary>空白以外の同じ文字が続く最長の長さ。</summary>
    public static int LongestRun(string text)
    {
        var longest = 0;
        var current = 0;
        string? previous = null;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            if (string.IsNullOrWhiteSpace(element))
            {
                previous = null;
                current = 0;
                continue;
            }

            current = element == previous ? current + 1 : 1;
            previous = element;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}
