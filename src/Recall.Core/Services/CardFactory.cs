using System.Text;
using System.Text.RegularExpressions;

namespace Recall.Services;

public sealed record DraftCard(string Front, string Back, string? Source, string Kind, double Score, int Order);

/// <summary>
/// Deterministic card extraction. Every card is quoted from the source text, so a card
/// can't claim something the document didn't say. The order is: a heading or bold run
/// followed by prose becomes a term/definition card, then an inline "term: definition"
/// line, then a copula sentence, and cloze only over a term the document already
/// established. Anything that looks like code, a caption, a page number, or a reference
/// list is dropped before any of that runs.
/// </summary>
public static partial class CardFactory
{
    private const int MaxFront = 90;
    private const int MaxBack = 260;

    /// <summary>A list answer is several sentences, so it gets more room than a definition.</summary>
    private const int MaxListBack = 400;
    private const int MaxTermWords = 7;

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

    [GeneratedRegex(@"^\s*(?:[\u2022\u25aa\u25e6*·\-\u2013\u2014]|\(?\d{1,2}[.)]|[a-zA-Z][.)]|o)\s+")]
    private static partial Regex BulletMarker();

    [GeneratedRegex(@"[{};]|//|\b(?:public|private|protected|static|void|class|new|return)\b\s*\w*\s*[({=]")]
    private static partial Regex CodeShaped();

    [GeneratedRegex(@"^(?:figure|fig\.?|table|chart|diagram|exhibit)\s*[\d.]*", RegexOptions.IgnoreCase)]
    private static partial Regex Caption();

    [GeneratedRegex(@"^(?:references?|bibliography|works cited|further reading|sources)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReferenceHeading();

    [GeneratedRegex(@"^(?:https?://|www\.)|\S+@\S+\.\w+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlOrEmail();

    private static readonly string[] Abbreviations =
    {
        "e.g.", "i.e.", "et al.", "vs.", "Fig.", "fig.", "No.", "no.", "Dr.", "Mr.", "Mrs.", "Ms.",
        "Prof.", "St.", "approx.", "cf.", "Eq.", "Sec.", "Ch.", "pp.", "p."
    };

    /// <summary>
    /// Placeholder nouns that name a slot in a code sample rather than a concept. "The name
    /// is a defined name for namespace" is a sentence about the sample, not about the subject.
    /// </summary>
    private static readonly HashSet<string> GenericTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "names", "value", "values", "type", "types", "data", "code", "number", "numbers",
        "item", "items", "line", "lines", "text", "string", "strings", "result", "results",
        "example", "examples", "output", "input", "size", "length", "keyword", "keywords",
        "statement", "statements", "program", "syntax", "form", "content", "part", "parts"
    };

    /// <summary>Words that open a clause, never a term. A front starting with one is prose.</summary>
    private static readonly HashSet<string> LeadInWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "which", "who", "whom", "whose", "when", "where", "why", "how", "solution", "answer",
        "example", "examples", "figure", "fig", "table", "chapter", "section", "part", "unit",
        "references", "bibliography", "note", "notes", "step", "steps", "given", "since", "then",
        "now", "also", "however", "therefore", "thus", "hence", "finally", "first", "second", "third",
        "next", "below", "above", "following", "instead", "moreover", "furthermore", "consider",
        "suppose", "assume", "let", "we", "you", "they", "it", "this", "these", "those", "there",
        "here", "in", "on", "at", "for", "from", "with", "by", "to", "of", "as", "if", "so", "but",
        "and", "or", "because", "while", "after", "before", "during", "through", "within", "between",
        "using", "define", "defining", "use", "lets", "try", "see", "shown", "showing",
        "sample", "problem", "problems", "practice", "exercise", "exercises"
    };

    /// <summary>Words that mark a fragment rather than a term: if the front contains one, it is a clause.</summary>
    private static readonly string[] ClauseMarkers =
    {
        " that ", " which ", " who ", " whose ", " where ", " when ", " because ", " while ",
        " and ", " or ", " but ", " is ", " are ", " was ", " were ", " has ", " have ", " had ",
        " can ", " will ", " would ", " should ", " may ", " might ", " must ", " to "
    };

    /// <summary>Definition text that is a fragment of the sentence before it, not a definition.</summary>
    private static readonly string[] DefLeadIns =
    {
        "not ", "in this case", "given", "also ", "then ", "instead", "however",
        "therefore", "thus ", "this is ", "these are ", "it is ", "there are ", "there is "
    };

    /// <summary>
    /// A sentence that talks about the sample on the page, not about the subject.
    /// "The following class ____ shows an example..." asks about a variable name.
    /// </summary>
    private static readonly string[] ExampleLeadIns =
    {
        "the following", "following example", "given example", "example below", "below shows",
        "as shown", "the statement", "is declared", "are declared", "in this example",
        "the example", "the sample", "sample program", "the output", "the program below",
        "sample problem", "practice problem", "problem set"
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "that", "with", "from", "this", "these", "those", "which", "there", "their",
        "have", "will", "would", "could", "should", "about", "into", "than", "then", "them", "they",
        "when", "where", "while", "because", "between", "within", "after", "before", "other", "each",
        "such", "also", "been", "being", "were", "what", "your", "more", "most", "some", "many",
        "very", "only", "over", "under", "both", "does", "used", "using", "called", "same",
        "made", "make", "like", "just", "even", "well", "much", "through"
    };

    /// <summary>Answers that carry no information as a blank.</summary>
    private static readonly HashSet<string> EmptyAnswers = new(StringComparer.OrdinalIgnoreCase)
    {
        "automatically", "respectively", "dynamically", "accessible", "respective", "expressions",
        "returning", "represented", "represents", "invoked", "called", "defined", "created", "given",
        "following", "different", "various", "certain", "several", "usually", "generally", "often",
        "sometimes", "typically", "simply", "easily", "directly", "actually", "basically", "really",
        "approximately", "almost", "nearly", "roughly", "respectively",
        "one", "two", "three", "more", "less", "same", "other", "another", "each", "every"
    };

    private static readonly string[] Determiners =
    {
        "the ", "a ", "an ", "this ", "these ", "those ", "its ", "their "
    };

    /// <summary>Words that end a blank that swallowed the next word: "Constructors with".</summary>
    private static readonly HashSet<string> TrailingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "with", "of", "and", "or", "to", "for", "in", "on", "at", "by", "from", "that", "which",
        "is", "are", "was", "were", "be", "been", "can", "will", "would", "should", "may", "might",
        "must", "has", "have", "had", "help", "helps", "used", "use", "show", "shows", "make",
        "makes", "give", "gives", "include", "includes", "contain", "contains", "allow", "allows"
    };

    /// <summary>
    /// Participles that open a definition whose subject was left in the term: "Instances of
    /// classes are created using the new keyword" answers with "Created using the new keyword".
    /// </summary>
    private static readonly HashSet<string> PassiveOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "created", "defined", "used", "called", "given", "known", "made", "built", "passed",
        "returned", "declared", "invoked", "executed", "initialized", "initialised", "assigned",
        "imported", "accessed", "represented", "stored", "derived", "specified", "obtained"
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
        @"^(?<term>[^\u2014\u2013]{2,60}?)\s+[\u2014\u2013]\s+(?<def>.{15,240})$",
        RegexOptions.Compiled);

    private static readonly Regex DefinitionCalled = new(
        @"^(?<def>.{15,200}?)\s+(?:is|are)\s+called\s+(?<term>[^,.;]{2,50})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Turns PDF lines into paragraphs: drops running headers, footers, page numbers,
    /// captions, reference lists and code, then rejoins lines that were only wrapped.
    /// </summary>
    public static List<PdfLine> PrepareDocument(IReadOnlyList<PdfLine> lines)
    {
        // A header or footer sits at the same text on many pages. Count across the document.
        var repeats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var key = line.Text.Trim();
            if (key.Length is > 0 and < 80)
                repeats[key] = repeats.GetValueOrDefault(key) + 1;
        }

        var kept = new List<PdfLine>(lines.Count);
        var inReferences = false;

        foreach (var line in lines)
        {
            var text = line.Text.Trim();
            if (text.Length == 0) continue;

            if (ReferenceHeading().IsMatch(text)) { inReferences = true; continue; }
            if (inReferences) continue;

            if (repeats.TryGetValue(text, out var seen) && seen > 2 && text.Length < 80) continue;
            if (PageNumberOnly().IsMatch(text)) continue;
            if (Caption().IsMatch(text)) continue;
            if (UrlOrEmail().IsMatch(text)) continue;
            if (CodeShaped().IsMatch(text)) continue;
            if (text.Length < 3) continue;

            kept.Add(line with { Text = text });
        }

        // The bullet is stripped after the lines are rejoined: a bullet on the next line is
        // the one reliable sign that it starts a new item instead of continuing this one.
        // The flag is carried on the line so later passes can still see the list structure.
        return MergeWrapped(kept.Select(l => l with { ListItem = BulletMarker().IsMatch(l.Text) }).ToList())
            .Select(l => l with { Text = BulletMarker().Replace(l.Text, "", 1).Trim() })
            .Where(l => l.Text.Length >= 3)
            .ToList();
    }

    /// <summary>A body line that does not end a sentence continues on the next line.</summary>
    private static List<PdfLine> MergeWrapped(List<PdfLine> lines)
    {
        // The right edge of the text column, per page. A line that stops short of it ended
        // on purpose; only a line that reaches it was wrapped onto the next one.
        var margins = new Dictionary<int, double>();
        foreach (var line in lines)
        {
            if (line.Right <= 0 || line.Text.Length < 30) continue;
            margins[line.Page] = Math.Max(margins.GetValueOrDefault(line.Page), line.Right);
        }

        var merged = new List<PdfLine>(lines.Count);
        PdfLine? pending = null;

        foreach (var line in lines)
        {
            if (pending is null) { pending = line; continue; }

            var sameBlock = line.Page == pending.Page
                            && Math.Abs(line.Size - pending.Size) < 0.6
                            && (Math.Abs(line.BoldRatio - pending.BoldRatio) < 0.35
                                || char.IsLower(line.Text[0]));

            var open = pending.Text.Length > 0 && !".!?:\"".Contains(pending.Text[^1]);

            if (sameBlock && open && Continues(pending, line, margins))
            {
                pending = pending with { Text = Join(pending.Text, line.Text), Right = line.Right };
                continue;
            }

            merged.Add(pending);
            pending = line;
        }

        if (pending is not null) merged.Add(pending);
        return merged;
    }

    /// <summary>
    /// Whether <paramref name="next"/> is the rest of <paramref name="pending"/>'s sentence.
    /// Three things say it is not: a bullet or list number on the next line, a step back to
    /// a narrower indent, and a pending line that never reached the right margin.
    /// </summary>
    private static bool Continues(PdfLine pending, PdfLine next, Dictionary<int, double> margins)
    {
        if (next.ListItem || BulletMarker().IsMatch(next.Text)) return false;
        if (next.Left < pending.Left - 2) return false;

        // Plain text has no geometry: every line came in at zero, so nothing to measure.
        if (pending.Right <= 0) return true;
        if (!margins.TryGetValue(pending.Page, out var margin) || margin < 200) return true;

        var slack = Math.Max(6.0, margin * 0.02);
        return pending.Right >= margin - slack;
    }

    private static string Join(string left, string right)
    {
        if (left.EndsWith('-') && right.Length > 0 && char.IsLower(right[0]))
            return left[..^1] + right;
        return left + " " + right;
    }

    public static List<DraftCard> FromText(string? raw, int max, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(raw) || max <= 0) return new List<DraftCard>();

        var text = Clean(raw);
        var lines = text.Split('\n')
            .Select(t => new PdfLine(0, t.Trim(), 0, 0, 0))
            .Where(l => l.Text.Length > 0)
            .ToList();

        return FromLines(lines, max, source);
    }

    /// <summary>Extracts cards from one page of prepared lines.</summary>
    public static List<DraftCard> FromLines(IReadOnlyList<PdfLine> lines, int max, string? source = null)
    {
        if (max <= 0 || lines.Count == 0) return new List<DraftCard>();

        var body = BodySize(lines);
        var drafts = new List<DraftCard>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;

        // Prose for the two sentence-level passes. It is split before the term set is built
        // because a phrase the document repeats is one of its terms.
        var sentences = SplitSentences(string.Join('\n', lines.Select(l => l.Text)));

        var terms = CollectTerms(lines, body);
        foreach (var phrase in RepeatedPhrases(sentences)) terms.Add(phrase);

        // 1. A heading over a run of bullets. The bullets are the answer, so the heading is
        // the front and the run is the back. This runs before the definition pass because the
        // "definition" under such a heading is the first bullet, which is not a definition.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.ListItem || !IsTermLine(line, body)) continue;

            var term = TidyTerm(line.Text);
            if (!IsUsableTerm(term)) continue;

            var items = new List<string>();
            var used = 0;

            for (var k = i + 1; k < lines.Count && lines[k].Page == line.Page; k++)
            {
                var next = lines[k];

                // A lead-in line ("For a normal distribution:") sits between the heading and its
                // bullets. Anything else that is not a bullet ends the list.
                if (!next.ListItem)
                {
                    if (items.Count > 0 && next.Text.EndsWith(':')) continue;
                    break;
                }

                var item = next.Text.Trim();
                if (item.Length is < 3 or > 180) continue;
                if (CodeShaped().IsMatch(item)) continue;
                if (MathGlyph().IsMatch(item)) continue;

                var numbered = $"{items.Count + 1}. {item.TrimEnd('.', ';')}.";

                // Three items are the minimum for a list card, so the cap only applies once the
                // card has enough in it to ask about.
                if (items.Count >= 3 && used + numbered.Length > MaxListBack) break;

                used += numbered.Length + 1;
                items.Add(numbered);
            }

            // Two bullets under a heading is a sentence split in half, not a list to recall.
            if (items.Count < 3) continue;

            order++;
            Add(new DraftCard(term, string.Join(' ', items), source, "list", 1.45, order), drafts, seen);
        }

        // 2. A definition written as an assignment: "median = the middle value of a data set".
        // The left side is the question, the right side is the answer.
        foreach (var line in lines)
        {
            if (!TryVariable(line.Text, out var lhs, out var rhs)) continue;

            order++;
            Add(new DraftCard($"What does {lhs} represent?", rhs, source, "variable", 1.25, order), drafts, seen);
        }

        // 3. A heading or bold line with prose under it.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!IsTermLine(line, body)) continue;
            if (i + 1 >= lines.Count) continue;

            var next = lines[i + 1];
            if (next.Page != line.Page) continue;
            if (IsTermLine(next, body)) continue;
            if (next.Text.Length < 20) continue;

            var term = TidyTerm(line.Text);
            var def = TidyDefinition(DefinitionFrom(lines, i + 1, body), term);
            if (!IsUsableTerm(term) || !IsUsableDefinition(def, term)) continue;

            order++;
            Add(new DraftCard(term, def, source, "definition", 1.4, order), drafts, seen);
        }

        // 4. Prose: inline definitions first, then copula sentences, then cloze.
        var alreadyDefined = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sentence in sentences)
        {
            order++;
            if (!TryDefinition(sentence, out var draft)) continue;

            // The same sentence must not come back as a cloze card: one sentence, one card.
            alreadyDefined.Add(TextMatch.Normalize(sentence));
            Add(draft with { Order = order, Source = source }, drafts, seen);
        }

        if (drafts.Count < max)
        {
            var blanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var sentence in sentences)
            {
                if (drafts.Count >= max * 2) break;
                if (alreadyDefined.Contains(TextMatch.Normalize(sentence))) continue;
                order++;
                if (!TryCloze(sentence, terms, out var draft)) continue;

                // Six blanks all answered "class" is repetition, not revision.
                var used = blanks.GetValueOrDefault(draft.Back);
                if (used >= 2) continue;
                blanks[draft.Back] = used + 1;

                Add(draft with { Order = order, Source = source }, drafts, seen);
            }
        }

        return Dedupe(drafts)
            .OrderByDescending(d => d.Score)
            .ThenBy(d => d.Order)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// "Uniform Probability Distribution" and "Uniform distribution (aka Rectangular
    /// probability distribution)" are one card. Keep the higher scoring of the two.
    /// </summary>
    private static List<DraftCard> Dedupe(List<DraftCard> drafts)
    {
        var kept = new List<DraftCard>(drafts.Count);

        foreach (var draft in drafts.OrderByDescending(d => d.Score).ThenBy(d => d.Order))
        {
            // "What does X represent?" and "What does μ represent?" share every word worth
            // comparing, but they ask about different symbols. Exact fronts are already
            // rejected by the caller, so these are left alone.
            if (draft.Kind == "variable") { kept.Add(draft); continue; }

            var tokens = Tokens(StripParenthetical(draft.Front));

            var duplicate = kept.Any(k =>
            {
                var other = Tokens(StripParenthetical(k.Front));

                // Intersect uses the default comparer, so pass the case-insensitive one.
                var shared = tokens.Intersect(other, StringComparer.OrdinalIgnoreCase).Count();

                // "constructor" and "Constructors" are one card even though each has a
                // single token, which the overlap ratio below would never reach.
                if (tokens.Count == 1 && other.Count == 1) return shared == 1;

                if (tokens.Count < 2 || other.Count < 2) return false;
                return shared >= 2 && shared >= Math.Min(tokens.Count, other.Count) * 0.75;
            });

            if (!duplicate) kept.Add(draft);
        }

        return kept;
    }

    /// <summary>
    /// "Exponential distribution (aka negative exponential distribution)" is the same card as
    /// the heading "Exponential Probability Distribution", so the aside is dropped before the
    /// two fronts are compared.
    /// </summary>
    private static string StripParenthetical(string text)
    {
        var open = text.IndexOf('(');
        if (open <= 0) return text;
        var close = text.IndexOf(')', open);
        if (close < open) return text;
        return (text[..open] + " " + text[(close + 1)..]).Trim();
    }

    private static HashSet<string> Tokens(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in Regex.Split(text, @"[^A-Za-z]+"))
        {
            if (word.Length < 3) continue;
            if (StopWords.Contains(word)) continue;
            set.Add(word.EndsWith("ss", StringComparison.Ordinal) || !word.EndsWith('s') ? word : word[..^1]);
        }
        return set;
    }

    /// <summary>
    /// The definition under a term is the prose that follows it, up to the end of the first
    /// sentence. Stopping at one line leaves "for a." on the card.
    /// </summary>
    private static string DefinitionFrom(IReadOnlyList<PdfLine> lines, int start, double body)
    {
        var sb = new StringBuilder();
        var page = lines[start].Page;

        for (var i = start; i < lines.Count && i < start + 4; i++)
        {
            var line = lines[i];
            if (line.Page != page) break;
            if (i > start && IsTermLine(line, body)) break;

            if (sb.Length > 0) sb.Append(' ');
            sb.Append(line.Text);

            if (sb.Length >= 40 && ".!?".Contains(sb[^1])) break;
            if (sb.Length >= MaxBack) break;
        }

        return sb.ToString();
    }

    private static void Add(DraftCard draft, List<DraftCard> drafts, HashSet<string> seen)
    {
        if (draft.Front.Length == 0 || draft.Back.Length == 0) return;
        if (!seen.Add(TextMatch.Normalize(draft.Front))) return;
        drafts.Add(draft);
    }

    /// <summary>The size most of the prose is set in. Headings are bigger than this.</summary>
    private static double BodySize(IReadOnlyList<PdfLine> lines)
    {
        var weighted = lines
            .Where(l => l.Text.Length >= 25 && l.Size > 0)
            .GroupBy(l => Math.Round(l.Size, 1))
            .Select(g => (Size: g.Key, Chars: g.Sum(l => l.Text.Length)))
            .OrderByDescending(g => g.Chars)
            .ToList();

        return weighted.Count == 0 ? 0 : weighted[0].Size;
    }

    private static bool IsTermLine(PdfLine line, double body)
    {
        var text = line.Text.Trim();
        if (text.Length is < 2 or > 70) return false;
        if (text[^1] is '.' or ',' or ';' or '?' or '!') return false;
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxTermWords) return false;

        var larger = body > 0 && line.Size >= body * 1.12;
        return larger || line.IsBold;
    }

    /// <summary>Terms the document itself highlighted. Cloze only blanks one of these.</summary>
    private static HashSet<string> CollectTerms(IReadOnlyList<PdfLine> lines, double body)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (!IsTermLine(line, body)) continue;
            var term = TidyTerm(line.Text);
            if (IsUsableTerm(term)) terms.Add(term);
        }
        return terms;
    }

    /// <summary>
    /// A phrase the document repeats is one of its terms even when it is set in plain
    /// lowercase: "standard deviation", "random variable". Cloze may blank those, which is
    /// what turns a page of numbered properties into answerable cards.
    /// </summary>
    private static List<string> RepeatedPhrases(IEnumerable<string> sentences)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var sentence in sentences)
        {
            var words = Regex.Split(sentence, @"[^A-Za-z]+");

            for (var i = 0; i < words.Length; i++)
            {
                for (var span = 2; span <= 3 && i + span <= words.Length; span++)
                {
                    var phrase = words[i..(i + span)];
                    if (phrase.Any(w => w.Length < 4 || StopWords.Contains(w))) continue;

                    var key = string.Join(' ', phrase);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }
        }

        return counts.Where(p => p.Value >= 2).Select(p => p.Key).ToList();
    }

    public static string Clean(string raw)
    {
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        text = HyphenBreak().Replace(text, "$1$2");

        var lines = text.Split('\n');

        // Running headers and footers repeat; drop them.
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
            if (Caption().IsMatch(key)) continue;
            if (ReferenceHeading().IsMatch(key)) continue;

            kept.Add(BulletMarker().Replace(key, "", 1).Trim());
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

    private static bool TryDefinition(string sentence, out DraftCard draft)
    {
        draft = new DraftCard("", "", null, "definition", 0, 0);
        var s = sentence.Trim().TrimEnd('.');
        if (s.Length < 30) return false;
        if (s.Contains('?')) return false;
        if (CodeShaped().IsMatch(s)) return false;

        string term;
        string def;
        double score;

        if (TryMatch(DefinitionCalled, s, out var calledDef, out var calledTerm, invert: true))
        {
            term = calledTerm;
            def = calledDef;
            score = 1.15;
        }
        else if (TryMatch(DefinitionColon, s, out term, out def) ||
                 TryMatch(DefinitionDash, s, out term, out def))
        {
            score = 1.3;
        }
        else if (TryMatch(DefinitionIs, s, out term, out def) ||
                 TryMatch(DefinitionVerb, s, out term, out def))
        {
            score = 1.2;
        }
        else
        {
            return false;
        }

        term = TidyTerm(term);
        def = TidyDefinition(def, term);
        if (!IsUsableTerm(term) || !IsUsableDefinition(def, term)) return false;

        if (def.Length is >= 40 and <= 160) score += 0.1;
        draft = new DraftCard(term, def, null, "definition", score, 0);
        return true;
    }

    /// <summary>
    /// "median = the middle value of a data set" and "speed = distance / time" are definitions
    /// written as assignments. Two equals signs is a formula, a numeric right side is
    /// arithmetic, and a left side longer than three words is a sentence.
    /// </summary>
    private static bool TryVariable(string line, out string lhs, out string rhs)
    {
        lhs = "";
        rhs = "";

        var s = line.Trim();
        if (s.Length is < 8 or > 200) return false;
        if (s.Count(c => c == '=') != 1) return false;
        if (CodeShaped().IsMatch(s)) return false;

        var at = s.IndexOf('=');
        var left = s[..at].Trim();
        var right = s[(at + 1)..].Trim().TrimEnd(';', ',');

        // "where Z = standard score": the connector belongs to the sentence, not the symbol.
        if (left.StartsWith("where ", StringComparison.OrdinalIgnoreCase))
            left = left["where ".Length..].Trim();

        if (left.Length is < 1 or > 34) return false;
        if (right.Length is < 4 or > 120) return false;

        // Operators mean a formula, not a meaning. "/" is left in: "distance / time".
        if (right.IndexOfAny(new[] { '=', '{', '}', '<', '>', '|', '+', '*' }) >= 0) return false;
        if (MathGlyph().IsMatch(right)) return false;

        var words = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 3) return false;
        if (!char.IsLetter(left[0])) return false;
        if (LeadInWords.Contains(words[0])) return false;
        if (left.IndexOfAny(new[] { ',', '.', ':', '?', '!', '(', ')' }) >= 0) return false;
        if (words.Length == 1 && GenericTerms.Contains(left)) return false;

        // "x = 5" is arithmetic. A meaning has words in it.
        if (!right.Any(char.IsLetter)) return false;

        var definition = TidyDefinition(right);
        if (definition.Length < 6) return false;

        lhs = left;
        rhs = definition;
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

    /// <summary>
    /// Cloze only over a term the document already highlighted, or a capitalised phrase in
    /// the middle of the sentence. Blanking an adverb makes a card nobody can answer.
    /// </summary>
    private static bool TryCloze(string sentence, HashSet<string> terms, out DraftCard draft)
    {
        draft = new DraftCard("", "", null, "cloze", 0, 0);
        var s = sentence.Trim();
        if (s.Length is < 60 or > 200) return false;
        if (s.Contains('?')) return false;
        if (s.EndsWith(':')) return false; // a line that introduces code, not a statement
        if (s.Count(c => c == ':') > 1) return false; // a data dump, not a sentence
        if (s.Contains("Press any key", StringComparison.OrdinalIgnoreCase)) return false;
        if (CodeShaped().IsMatch(s)) return false;

        // A sentence about the sample on the page is not a fact about the subject.
        var lower = s.ToLowerInvariant();
        foreach (var lead in ExampleLeadIns)
            if (lower.Contains(lead, StringComparison.Ordinal))
                return false;

        // A formula, a table row, or a bulleted list that lost its bullet.
        if (s.Contains('=')) return false;
        if (MathGlyph().IsMatch(s)) return false;
        if (AdverbRun().IsMatch(s)) return false;

        var span = PickClozeSpan(s, terms);
        if (span is null) return false;

        var (index, length) = span.Value;
        var blanked = s[..index] + "____" + s[(index + length)..];
        var answer = s.Substring(index, length).Trim();
        if (answer.Length < 3) return false;

        // "____ and variables that are not intended to be used outside of the class
        // can be hidden." The blank swallowed half a compound subject, so the sentence
        // no longer reads as a statement.
        if (index == 0)
        {
            var after = blanked[4..];
            if (after.StartsWith(" and ", StringComparison.OrdinalIgnoreCase) ||
                after.StartsWith(" or ", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!IsUsableClozeAnswer(answer)) return false;

        // The blank has to remove the answer from the sentence. "A ____ or structure can specify
        // how accessible each of its members is to code outside of the class or structure" still
        // shows the word, so the card asks nothing the reader can't already read.
        if (IndexOfWord(blanked, answer) >= 0) return false;

        draft = new DraftCard(blanked, answer, null, "cloze", 0.5, 0);
        return true;
    }

    /// <summary>An answer has to stand alone as the thing being asked for.</summary>
    private static bool IsUsableClozeAnswer(string answer)
    {
        var lower = answer.ToLowerInvariant();
        if (EmptyAnswers.Contains(answer)) return false;
        if (lower is "the following" or "following") return false;

        foreach (var determiner in Determiners)
            if (lower.StartsWith(determiner, StringComparison.Ordinal))
                return false;

        var words = answer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 4) return false;
        if (words.All(StopWords.Contains)) return false;

        // "Constructors with", "Namespaces help" - the blank swallowed the next word.
        var last = words[^1].ToLowerInvariant();
        if (TrailingWords.Contains(last)) return false;

        // "Defining", "Returning", "Invoked" - a verb form is a fragment, not an answer.
        if (words.Length == 1 && (last.EndsWith("ing", StringComparison.Ordinal) ||
                                  last.EndsWith("ed", StringComparison.Ordinal)))
            return false;

        return true;
    }

    /// <summary>
    /// A run of capitalised words. It stops at the first lowercase word, because
    /// "Lysosomes contain hydrolytic" is a subject plus its verb, not a term.
    /// </summary>
    [GeneratedRegex(@"\b[A-Z][a-z]{2,}(?:\s+[A-Z][a-z]{2,})*\b")]
    private static partial Regex CapitalPhrase();

    /// <summary>
    /// Mathematical alphanumerics and arrows, which only appear in formulas. .NET regex
    /// escapes are four hex digits, so the astral block is an alternation on its surrogate
    /// pair: inside a character class it would be read as class subtraction and never match.
    /// </summary>
    [GeneratedRegex(@"[\u2190-\u21FF\u2200-\u22FF]|\uD835[\uDC00-\uDFFF]")]
    private static partial Regex MathGlyph();

    /// <summary>
    /// "Thus, Therefore, Sample Problem 1:" is two list fragments glued together by the
    /// line rejoin, not a sentence.
    /// </summary>
    [GeneratedRegex(@"^[A-Z][a-z]+,\s+[A-Z][a-z]+,")]
    private static partial Regex AdverbRun();

    private static (int Index, int Length)? PickClozeSpan(string sentence, HashSet<string> terms)
    {
        // Best case: a term the document set in bold or in a heading.
        var best = (Index: -1, Length: 0);
        foreach (var term in terms)
        {
            if (term.Length is < 4 or > 40) continue;
            var at = IndexOfWord(sentence, term);
            if (at <= 0) continue;
            if (term.Length > best.Length) best = (at, term.Length);
        }
        if (best.Index >= 0) return best;

        // Otherwise a capitalised phrase, which in running prose is a name or a class.
        foreach (Match match in CapitalPhrase().Matches(sentence))
        {
            var index = match.Index;
            var value = match.Value;

            // "The Golgi apparatus" — blank the noun, not the article in front of it.
            foreach (var determiner in Determiners)
            {
                if (!value.StartsWith(determiner, StringComparison.OrdinalIgnoreCase)) continue;
                index += determiner.Length;
                value = value[determiner.Length..];
                break;
            }

            if (value.Length < 4) continue;
            if (StopWords.Contains(value)) continue;
            if (LeadInWords.Contains(value.Split(' ')[0])) continue;

            // A sentence that opens on the term is still a fair blank, as long as the term
            // is substantial. "The process..." is not, "Chloroplasts" is.
            if (index == 0 && value.Length < 6) continue;

            if (value.Length > best.Length) best = (index, value.Length);
        }
        if (best.Index >= 0 && best.Length >= 4) return best;

        return null;
    }

    /// <summary>
    /// "class" must not be found inside "classes": blanking it would leave "____es" on the card.
    /// </summary>
    private static int IndexOfWord(string sentence, string term)
    {
        var from = 0;
        while (from < sentence.Length)
        {
            var at = sentence.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;

            var end = at + term.Length;
            var startsWord = at == 0 || !char.IsLetter(sentence[at - 1]);
            var endsWord = end >= sentence.Length || !char.IsLetter(sentence[end]);
            if (startsWord && endsWord) return at;

            from = at + 1;
        }

        return -1;
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

    private static string TidyDefinition(string def, string term = "")
    {
        var d = def.Trim().Trim('-', '\u2014', '\u2013', ':', ' ');

        // "Encapsulation \u2014 Encapsulation is the process of..." reads as a stutter.
        if (term.Length > 0)
        {
            foreach (var copula in new[] { " is ", " are ", " refers to ", " means " })
            {
                var prefix = term + copula;
                if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    d = d[prefix.Length..];
                    break;
                }
            }
        }

        if (d.Length > MaxBack)
        {
            // Cut on a sentence boundary when there is one, so a long definition reads as
            // whole sentences instead of stopping mid-word.
            var window = d[..MaxBack];
            var stop = window.LastIndexOfAny(new[] { '.', '!', '?' });
            d = stop > 60 ? window[..(stop + 1)] : window.TrimEnd() + "\u2026";
        }

        if (d.Length > 0) d = char.ToUpperInvariant(d[0]) + d[1..];
        if (d.Length > 0 && !".!?\u2026".Contains(d[^1])) d += ".";
        return d;
    }

    private static bool IsUsableTerm(string term)
    {
        if (term.Length is < 3 or > MaxFront) return false;

        var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > MaxTermWords) return false;
        if (LeadInWords.Contains(words[0])) return false;
        if (term.Any(char.IsDigit)) return false;
        if (!char.IsLetter(term[0])) return false;
        if (term.Contains("http", StringComparison.OrdinalIgnoreCase)) return false;
        if (term.Contains('?') || term.Contains('!')) return false;

        // "arguments_list" is an identifier in a code sample, not a concept.
        if (term.Contains('_')) return false;

        // A clause is not a term. "Methods and variables that" is prose wearing a heading.
        var padded = " " + term.ToLowerInvariant() + " ";
        foreach (var marker in ClauseMarkers)
            if (padded.Contains(marker, StringComparison.Ordinal))
                return false;

        if (term.StartsWith("Figure", StringComparison.OrdinalIgnoreCase)) return false;
        if (term.StartsWith("Table", StringComparison.OrdinalIgnoreCase)) return false;

        // A single placeholder noun is never a question worth asking.
        if (words.Length == 1 && GenericTerms.Contains(term)) return false;
        return true;
    }

    private static bool IsUsableDefinition(string def, string term)
    {
        if (def.Length is < 15 or > MaxBack + 1) return false;
        if (!def.Contains(' ')) return false;
        if (string.Equals(def.TrimEnd('.'), term, StringComparison.OrdinalIgnoreCase)) return false;
        if (def.Contains("http", StringComparison.OrdinalIgnoreCase)) return false;
        if (def.Contains('?')) return false;

        // A formula is not a definition: "(𝑏𝑏−𝑎𝑎) and the total area under the curve".
        if (MathGlyph().IsMatch(def)) return false;

        var lower = def.ToLowerInvariant();
        foreach (var lead in DefLeadIns)
            if (lower.StartsWith(lead, StringComparison.Ordinal))
                return false;

        // "Instances of classes are created using the new keyword." The definition opens on a
        // participle with its auxiliary left behind, so the card reads as half a sentence.
        var first = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (PassiveOpeners.Contains(first.TrimEnd(',', '.', ';', ':'))) return false;

        return true;
    }
}
