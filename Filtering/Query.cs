using System.Text.RegularExpressions;

namespace PartySieve.Filtering;

public sealed class QueryParseException(string message) : Exception(message);

/// <summary>
/// 検索式。書き方:
///   語 語          … AND（両方を含む）
///   語 OR 語 / 語 | 語 … OR
///   -語 / NOT 語    … 含まない
///   ( … )          … まとめる
///   "空白 を含む語" … そのままの語
///   /正規表現/      … 正規表現（大文字小文字は区別しない）
/// 普通の語は、全角半角・大文字小文字・カタカナひらがなを区別しない。
/// </summary>
public abstract class Query
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    public abstract bool Matches(string raw, string normalized);

    public bool Matches(string text) => this.Matches(text, TextNormalizer.Normalize(text));

    /// <summary>空の式は null を返す。</summary>
    public static Query? Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var parser = new Parser(Tokenize(source));
        var query = parser.ParseOr();
        if (!parser.AtEnd)
        {
            throw new QueryParseException("閉じかっこ「)」が多すぎます。");
        }

        return query;
    }

    public static bool TryParse(string? source, out Query? query, out string? error)
    {
        try
        {
            query = Parse(source);
            error = null;
            return true;
        }
        catch (QueryParseException ex)
        {
            query = null;
            error = ex.Message;
            return false;
        }
    }

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '(':
                    tokens.Add(new Token(TokenKind.LParen));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new Token(TokenKind.RParen));
                    i++;
                    continue;
                case '|':
                    tokens.Add(new Token(TokenKind.Or));
                    i++;
                    continue;
                case '-' when i + 1 < source.Length && !char.IsWhiteSpace(source[i + 1]):
                    tokens.Add(new Token(TokenKind.Not));
                    i++;
                    continue;
                case '"':
                {
                    var end = source.IndexOf('"', i + 1);
                    if (end < 0)
                    {
                        throw new QueryParseException("「\"」が閉じていません。");
                    }

                    tokens.Add(new Token(TokenKind.Term, source[(i + 1)..end]));
                    i = end + 1;
                    continue;
                }

                case '/':
                {
                    var end = FindRegexEnd(source, i + 1);
                    if (end > i + 1)
                    {
                        tokens.Add(new Token(TokenKind.Regex, source[(i + 1)..end]));
                        i = end + 1;
                        continue;
                    }

                    break;
                }
            }

            var start = i;
            while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] is not ('(' or ')' or '|'))
            {
                i++;
            }

            var word = source[start..i];
            tokens.Add(word switch
            {
                "OR" => new Token(TokenKind.Or),
                "AND" => new Token(TokenKind.And),
                "NOT" => new Token(TokenKind.Not),
                _ => new Token(TokenKind.Term, word),
            });
        }

        return tokens;
    }

    private static int FindRegexEnd(string source, int from)
    {
        for (var i = from; i < source.Length; i++)
        {
            if (source[i] == '\\')
            {
                i++;
                continue;
            }

            if (source[i] == '/')
            {
                return i;
            }
        }

        return -1;
    }

    private enum TokenKind
    {
        Term,
        Regex,
        And,
        Or,
        Not,
        LParen,
        RParen,
    }

    private readonly record struct Token(TokenKind Kind, string Text = "");

    private sealed class Parser(List<Token> tokens)
    {
        private int position;

        public bool AtEnd => this.position >= tokens.Count;

        private Token? Peek => this.AtEnd ? null : tokens[this.position];

        public Query ParseOr()
        {
            var parts = new List<Query> { this.ParseAnd() };
            while (this.Peek?.Kind == TokenKind.Or)
            {
                this.position++;
                parts.Add(this.ParseAnd());
            }

            return parts.Count == 1 ? parts[0] : new OrQuery(parts);
        }

        private Query ParseAnd()
        {
            var parts = new List<Query> { this.ParseUnary() };
            while (this.Peek is { } next && next.Kind is not (TokenKind.Or or TokenKind.RParen))
            {
                if (next.Kind == TokenKind.And)
                {
                    this.position++;
                }

                parts.Add(this.ParseUnary());
            }

            return parts.Count == 1 ? parts[0] : new AndQuery(parts);
        }

        private Query ParseUnary()
        {
            if (this.Peek is not { } token)
            {
                throw new QueryParseException("式が途中で終わっています。");
            }

            this.position++;
            switch (token.Kind)
            {
                case TokenKind.Not:
                    return new NotQuery(this.ParseUnary());
                case TokenKind.LParen:
                {
                    var inner = this.ParseOr();
                    if (this.Peek?.Kind != TokenKind.RParen)
                    {
                        throw new QueryParseException("かっこ「(」が閉じていません。");
                    }

                    this.position++;
                    return inner;
                }

                case TokenKind.Term:
                    return new TermQuery(token.Text);
                case TokenKind.Regex:
                    try
                    {
                        return new RegexQuery(new Regex(
                            token.Text,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            RegexTimeout));
                    }
                    catch (ArgumentException ex)
                    {
                        throw new QueryParseException($"正規表現 /{token.Text}/ が不正です: {ex.Message}");
                    }

                case TokenKind.And:
                case TokenKind.Or:
                    throw new QueryParseException("AND / OR の前後に語がありません。");
                default:
                    throw new QueryParseException("「)」の位置が不正です。");
            }
        }
    }

    private sealed class TermQuery(string term) : Query
    {
        private readonly string normalizedTerm = TextNormalizer.Normalize(term);

        public override bool Matches(string raw, string normalized) =>
            normalized.Contains(this.normalizedTerm, StringComparison.Ordinal);
    }

    private sealed class RegexQuery(Regex regex) : Query
    {
        public override bool Matches(string raw, string normalized)
        {
            try
            {
                return regex.IsMatch(raw) || regex.IsMatch(normalized);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }

    private sealed class NotQuery(Query inner) : Query
    {
        public override bool Matches(string raw, string normalized) => !inner.Matches(raw, normalized);
    }

    private sealed class AndQuery(List<Query> parts) : Query
    {
        public override bool Matches(string raw, string normalized) =>
            parts.TrueForAll(p => p.Matches(raw, normalized));
    }

    private sealed class OrQuery(List<Query> parts) : Query
    {
        public override bool Matches(string raw, string normalized) =>
            parts.Exists(p => p.Matches(raw, normalized));
    }
}
