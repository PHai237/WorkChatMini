using System.Globalization;
using System.Text;

namespace WorkChatMini;

// Accent-insensitive matching so "dau pha" finds "Đấu Phá Thương Khung" without typing diacritics.
internal static class SearchTextFolding
{
    internal static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        try
        {
            string decomposed = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                builder.Append(c switch { '\u0111' => 'd', '\u0110' => 'D', _ => c }); // đ, Đ have no decomposition
            }
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return text; // lone surrogates etc.: fall back to the raw text
        }
    }

    internal static bool Matches(string? haystack, string foldedNeedle) =>
        Fold(haystack).Contains(foldedNeedle, StringComparison.OrdinalIgnoreCase);
}
