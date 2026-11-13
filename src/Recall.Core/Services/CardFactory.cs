using System.Text.RegularExpressions;

namespace Recall.Services;

public sealed record DraftCard(string Front, string Back, string? Source, string Kind, double Score, int Order);

/// <summary>
/// Deterministic card extraction. Every card is quoted from the source text, so a card
/// can't claim something the document didn't say. Definition patterns run first, then
/// cloze deletion covers the remaining prose.
/// </summary>
public static partial class CardFactory
{
    private const int MaxFront = 90;
    private const int MaxBack = 260;

    [GeneratedRegex(@"(\w)-\n(\w)")]
    private static partial Regex HyphenBreak();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex SpaceRun();

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlankRun();

    [GeneratedRegex(@"^(?:page\s*)?\d{1,4}$", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumberOnly();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z0-9""'(])")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"^\s*(?:[•▪◦*·\-–—]|\(?\d{1,2}[.)]|[a-zA-Z][.)])\s+")]
    private static partial Regex BulletMarker();

    private static readonly string[] Abbreviations =
    {
        "e.g.", "i.e.", "et al.", "vs.", "Fig.", "fig.", "No.", "no.", "Dr.", "Mr.", "Mrs.", "Ms.",
        "Prof.", "St.", "approx.", "cf.", "Eq.", "Sec.", "Ch.", "pp.", "p."
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "that", "with", "from", "this", "these", "those", "which", "there", "their",
        "have", "will", "would", "could", "should", "about", "into", "than", "then", "them", "they",
        "when", "where", "while", "because", "between", "within", "after", "before", "other", "each",
        "such", "also", "been", "being", "were", "what", "your", "more", "most", "some", "many",
        "very", "only", "over", "under", "both", "does", "used", "using", "called", "same", "such",
        "made", "make", "like", "just", "even", "well", "much", "through"
    };

    private static readonly Regex DefinitionIs = new(
        @"^(?<term>[A-Z][^.;:!?]{2,70}?)\s+(?:is|are)\s+(?<def>.{15,240})$",
        RegexOptions.Compiled);

    private static readonly Regex DefinitionVerb = new(
        @"^(?<term>[A-Z][^.;:!?]{2,60}?)\s+(?:refers to|means|describes|is defined as|consists of)\s+(?<def>.{12,240})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DefinitionColon = new(
        @"^(?<term>[^:]{2,60}?)\s*:\s*(?<def>.{15,240})$",
        RegexOptions.Compiled);

    private static readonly Regex DefinitionDash = new(
        @"^(?<term>[^—–]{2,60}?)\s+[—–]\s+(?<def>.{15,240})$",
        RegexOptions.Compiled);

    private static readonly Regex DefinitionCalled = new(
        @"^(?<def>.{15,200}?)\s+(?:is|are)\s+called\s+(?<term>[^,.;]{2,50})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<DraftCard> FromText(string? raw, int max, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(raw) || max <= 0) return new List<DraftCard>();

        var text = Clean(raw);
        var sentences = SplitSentences(text);
        var drafts = new List<DraftCard>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var usedByDefinition = new HashSet<int>();
        var order = 0;

        for (var i = 0; i < sentences.Count; i++)
        {
            order++;
            if (TryDefinition(sentences[i], out var draft))
            {
                Add(draft with { Order = order, Source = source }, drafts, seen);
                usedByDefinition.Add(i);
            }
        }

        if (drafts.Count < max)
        {
            for (var i = 0; i < sentences.Count; i++)
            {
                if (usedByDefinition.Contains(i)) continue; // already covered by a definition card
                order++;
                if (drafts.Count >= max * 2) break;
                if (TryCloze(sentences[i], out var draft))
                    Add(draft with { Order = order, Source = source }, drafts, seen);
            }
        }

        return drafts
            .OrderByDescending(d => d.Score)
            .ThenBy(d => d.Order)
            .Take(max)
            .ToList();
    }

    private static void Add(DraftCard draft, List<DraftCard> drafts, HashSet<string> seen)
    {
        if (draft.Front.Length == 0 || draft.Back.Length == 0) return;
        if (!seen.Add(TextMatch.Normalize(draft.Front))) return;
        drafts.Add(draft);
    }

    public static string Clean(string raw)
    {
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        text = HyphenBreak().Replace(text, "$1$2");

        var lines = text.Split('\n');

        // Running headers and footers repeat on every page; drop them.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var key = line.Trim();
            if (key.Length is > 0 and < 80)
                counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var key = line.Trim();
            if (key.Length is > 0 and < 80 && counts[key] > 2) continue;
            if (PageNumberOnly().IsMatch(key)) continue;

            var stripped = StripBullet(key);
            if (LooksLikeHeading(stripped))
            {
                // A heading glued to body text yields terms like "Chapter 4 Mitochondria".
                kept.Add("");
                kept.Add(stripped);
                kept.Add("");
                continue;
            }

            kept.Add(stripped);
        }

        text = string.Join('\n', kept);
        text = BlankRun().Replace(text, "\n\n");

        // A single newline is line wrapping inside a paragraph, not a sentence break.
        text = text.Replace("\n\n", "\u0002");
        text = text.Replace('\n', ' ');
        text = text.Replace('\u0002', '\n');
        text = SpaceRun().Replace(text, " ");
        return text.Trim();
    }

    public static List<string> SplitSentences(string text)
    {
        var sentences = new List<string>();

        // Paragraph breaks are sentence breaks: a heading must not be glued to the
        // sentence that follows it.
        foreach (var paragraph in text.Split('\n'))
        {
            var guarded = paragraph;
            foreach (var abbr in Abbreviations)
                guarded = guarded.Replace(abbr, abbr.Replace(".", "\u0001"), StringComparison.Ordinal);

            foreach (var part in SentenceSplit().Split(guarded))
            {
                var sentence = part.Replace('\u0001', '.').Trim();
                if (sentence.Length >= 20) sentences.Add(sentence);
            }
        }

        return sentences;
    }

    private static string StripBullet(string line)
    {
        var stripped = BulletMarker().Replace(line, "", 1).Trim();
        return stripped.Length > 0 ? stripped : line;
    }

    private static bool LooksLikeHeading(string line)
    {
        if (line.Length is 0 or > 70) return false;
        if (line[^1] is '.' or ',' or ';' or ':') return false;

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 9) return false;

        // "Biology 101 - Chapter 4" style course headers
        if (words.Length <= 6 && line.Length <= 50 && line.Any(char.IsDigit)) return true;

        var capitalised = words.Count(w => w.Length > 0 && char.IsUpper(w[0]));
        return capitalised >= 2 && capitalised >= words.Length - 1;
    }

    private static bool TryDefinition(string sentence, out DraftCard draft)
    {
        draft = new DraftCard("", "", null, "definition", 0, 0);
        var s = sentence.Trim().TrimEnd('.');
        if (s.Length < 30) return false;

        string term;
        string def;

        if (TryMatch(DefinitionCalled, s, out var calledDef, out var calledTerm, invert: true))
        {
            term = calledTerm;
            def = calledDef;
        }
        else if (TryMatch(DefinitionIs, s, out term, out def) ||
                 TryMatch(DefinitionVerb, s, out term, out def) ||
                 TryMatch(DefinitionColon, s, out term, out def) ||
                 TryMatch(DefinitionDash, s, out term, out def))
        {
            // values already assigned by the successful call
        }
        else
        {
            return false;
        }

        term = TidyTerm(term);
        def = TidyDefinition(def);
        if (!IsUsableTerm(term) || !IsUsableDefinition(def, term)) return false;

        var score = 1.0 + (def.Length is >= 40 and <= 160 ? 0.2 : 0);
        draft = new DraftCard(term, def, null, "definition", score, 0);
        return true;
    }

    private static bool TryMatch(Regex regex, string s, out string first, out string second, bool invert = false)
    {
        first = "";
        second = "";
        var m = regex.Match(s);
        if (!m.Success) return false;

        var term = m.Groups["term"].Value.Trim();
        var def = m.Groups["def"].Value.Trim();
        if (term.Length == 0 || def.Length == 0) return false;

        if (invert)
        {
            first = def;
            second = term;
        }
        else
        {
            first = term;
            second = def;
        }
        return true;
    }

    private static bool TryCloze(string sentence, out DraftCard draft)
    {
        draft = new DraftCard("", "", null, "cloze", 0, 0);
        var s = sentence.Trim();
        if (s.Length is < 60 or > 200) return false;

        var span = PickClozeSpan(s);
        if (span is null) return false;

        var (index, length) = span.Value;
        var blanked = s[..index] + "____" + s[(index + length)..];
        var answer = s.Substring(index, length).Trim();
        if (answer.Length < 3) return false;

        draft = new DraftCard(blanked, answer, null, "cloze", 0.5, 0);
        return true;
    }

    [GeneratedRegex(@"\b[A-Z][a-z]{2,}(?:\s+(?:[A-Z][a-z]{2,}|[a-z]{4,}))?\b")]
    private static partial Regex CapitalPhrase();

    [GeneratedRegex(@"[A-Za-z][A-Za-z\-']{4,}")]
    private static partial Regex WordRun();

    private static (int Index, int Length)? PickClozeSpan(string sentence)
    {
        // A capitalised phrase mid-sentence is almost always the term worth blanking.
        var best = (Index: -1, Length: 0);
        foreach (Match match in CapitalPhrase().Matches(sentence))
        {
            if (match.Index == 0) continue;
            if (StopWords.Contains(match.Value)) continue;
            if (match.Length > best.Length) best = (match.Index, match.Length);
        }
        if (best.Index >= 0 && best.Length >= 4) return best;

        // Otherwise the longest content word, with a nudge towards the subject.
        var index = -1;
        var length = 0;
        var score = 0.0;
        foreach (Match match in WordRun().Matches(sentence))
        {
            if (StopWords.Contains(match.Value)) continue;
            var candidate = match.Value.Length / 10.0;
            if (char.IsUpper(match.Value[0])) candidate += match.Index == 0 ? 0.5 : 0.6;
            if (candidate > score)
            {
                score = candidate;
                index = match.Index;
                length = match.Value.Length;
            }
        }

        return index >= 0 && score >= 0.9 ? (index, length) : null;
    }

    private static string TidyTerm(string term)
    {
        var t = term.Trim().Trim('"', '\'', ' ', '-', ':', '.');
        foreach (var prefix in new[] { "The ", "A ", "An ", "This ", "These ", "That ", "Those " })
        {
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                t = t[prefix.Length..];
                break;
            }
        }
        return t.Trim();
    }

    private static string TidyDefinition(string def)
    {
        var d = def.Trim().Trim('-', '—', '–', ':', ' ');
        if (d.Length > 0) d = char.ToUpperInvariant(d[0]) + d[1..];
        if (d.Length > 0 && !".!?".Contains(d[^1])) d += ".";
        return d;
    }

    private static bool IsUsableTerm(string term)
    {
        if (term.Length is < 3 or > MaxFront) return false;
        if (term.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 10) return false;
        if (term.Any(char.IsDigit)) return false;
        if (!char.IsLetter(term[0])) return false;
        if (term.Contains("http", StringComparison.OrdinalIgnoreCase)) return false;
        if (term.StartsWith("Figure", StringComparison.OrdinalIgnoreCase)) return false;
        if (term.StartsWith("Table", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool IsUsableDefinition(string def, string term)
    {
        if (def.Length is < 15 or > MaxBack) return false;
        if (!def.Contains(' ')) return false;
        if (string.Equals(def.TrimEnd('.'), term, StringComparison.OrdinalIgnoreCase)) return false;
        if (def.Contains("http", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
