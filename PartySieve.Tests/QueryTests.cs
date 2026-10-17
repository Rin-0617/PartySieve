using PartySieve.Filtering;
using Xunit;

namespace PartySieve.Tests;

public class QueryTests
{
    [Theory]
    [InlineData("絶 クリア目的", "絶もうひとつ クリア目的 22時〜", true)]
    [InlineData("絶 クリア目的", "絶もうひとつ 練習", false)]
    [InlineData("クリア目的 | 消化", "週制限消化", true)]
    [InlineData("クリア目的 OR 消化", "練習", false)]
    [InlineData("絶 -練習", "絶 練習", false)]
    [InlineData("絶 NOT 練習", "絶 クリア", true)]
    [InlineData("絶 (クリア | 消化) -練習", "絶 消化", true)]
    [InlineData("絶 (クリア | 消化) -練習", "絶 消化 練習", false)]
    [InlineData("\"22 時\"", "開始 22 時", true)]
    [InlineData("/[0-9]{2}時/", "21時開始", true)]
    [InlineData("/[0-9]{2}時/", "夜開始", false)]
    public void Matches(string query, string text, bool expected)
    {
        Assert.Equal(expected, Query.Parse(query)!.Matches(text));
    }

    [Theory]
    [InlineData("ゴリラ", "ごりら式")]
    [InlineData("abc", "ＡＢＣ")]
    [InlineData("ＲＭＴ", "rmt")]
    public void IgnoresWidthCaseAndKana(string query, string text)
    {
        Assert.True(Query.Parse(query)!.Matches(text));
    }

    [Fact]
    public void FullWidthParenthesesAreLiteral()
    {
        Assert.True(Query.Parse("（ゴリラ）")!.Matches("散開（ごりら）"));
    }

    [Fact]
    public void EmptyIsNull()
    {
        Assert.Null(Query.Parse("   "));
    }

    [Theory]
    [InlineData("(絶")]
    [InlineData("絶)")]
    [InlineData("\"絶")]
    [InlineData("/[/")]
    [InlineData("絶 OR")]
    public void InvalidReportsError(string query)
    {
        Assert.False(Query.TryParse(query, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void SlashWithoutClosingIsLiteral()
    {
        Assert.True(Query.Parse("1/2")!.Matches("1/2 消化"));
    }
}
