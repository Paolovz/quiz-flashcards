using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Recall.Services;

/// Normalises and matches typed answers. Case, punctuation, diacritics and articles are
/// ignored. A stored answer often carries a definition after its key term — "Mitochondria,
/// the site of ATP synthesis" — and typing the key term alone is the same answer. A term
/// that is only part of a longer name is not: "cell" is not "cell membrane". Spelling is
/// not forgiven; a misspelled term is a miss.
public static partial class TextMatch
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRun();

    [GeneratedRegex(@"\s*(?:[,;:()\[\]]|—|–|\s-\s|/)\s*")]
    private static partial Regex KeyBreak();

    private static readonly HashSet<string> Articles = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "of", "to"
    };

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return SpaceRun().Replace(sb.ToString(), " ").Trim();
    }

    public static bool Matches(string? input, IEnumerable<string> accepted)
    {
        var typed = Normalize(input);
        if (typed.Length == 0) return false;

        var typedCore = StripArticles(typed);
        if (typedCore.Length == 0) return false;

        foreach (var candidate in accepted)
        {
            var target = Normalize(candidate);
            if (target.Length == 0) continue;
            if (typed == target) return true;

            var targetCore = StripArticles(target);
            if (targetCore.Length == 0) continue;
            if (typedCore == targetCore) return true;

            // The key term is whatever sits before the first separator in the stored
            // answer; the rest is detail the reader does not have to repeat.
            var head = StripArticles(Normalize(KeyBreak().Split(candidate, 2)[0]));
            if (head.Length == 0) continue;

            if (typedCore == head) return true;

            // The reader wrote the key term plus their own description.
            if (ContainsWords(typedCore, head)) return true;
        }
        return false;
    }

    /// Whole-word containment, so "cell" never matches "cell membrane" from the wrong side.
    private static bool ContainsWords(string haystack, string needle)
    {
        if (needle.Length < 4) return false;
        var words = haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var target = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var start = 0; start + target.Length <= words.Length; start++)
        {
            var match = true;
            for (var i = 0; i < target.Length; i++)
            {
                if (words[start + i] != target[i]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }

    private static string StripArticles(string s)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Where(p => !Articles.Contains(p))
                     .ToArray();
        return string.Join(' ', parts);
    }
}
