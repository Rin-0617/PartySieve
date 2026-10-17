using System.Text;

namespace PartySieve.Filtering;

/// <summary>
/// 全角半角・大文字小文字・カタカナひらがなの違いを吸収して比較するための正規化。
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            // カタカナ（ァ〜ヶ）をひらがなに寄せる。長音記号などはそのまま。
            builder.Append(c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 重複判定用のキー。空白と記号を落として、少しだけ文面を変えた連投もまとめる。
    /// </summary>
    public static string DuplicateKey(string? text)
    {
        var normalized = Normalize(text);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
