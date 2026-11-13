using Recall.Models;

namespace Recall.Services;

/// Text shapes the card editor round-trips through. Kept out of the view model so the
/// parsing rules can be tested without a UI.
public static class CardFormat
{
    /// One choice per line. A leading "*" marks a correct choice.
    public static (List<string> Choices, List<int> Correct) ParseChoices(string? text)
    {
        var choices = new List<string>();
        var correct = new List<int>();

        foreach (var raw in Lines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var isCorrect = line.StartsWith('*');
            var value = isCorrect ? line[1..].Trim() : line;
            if (value.Length == 0) continue;

            if (isCorrect) correct.Add(choices.Count);
            choices.Add(value);
        }

        return (choices, correct);
    }

    public static string FormatChoices(IReadOnlyList<string> choices, IReadOnlyList<int> correct)
    {
        if (choices.Count == 0) return "";
        var marked = correct.ToHashSet();
        return string.Join(Environment.NewLine,
            choices.Select((c, i) => marked.Contains(i) ? $"*{c}" : c));
    }

    /// One item per line, top to bottom is the correct sequence.
    public static List<string> ParseItems(string? text)
        => Lines(text).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public static string FormatItems(IReadOnlyList<string> items)
        => string.Join(Environment.NewLine, items);

    /// One pair per line, written "left = right".
    public static List<MatchPair> ParsePairs(string? text)
    {
        var pairs = new List<MatchPair>();

        foreach (var raw in Lines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var split = line.IndexOf('=');
            if (split <= 0 || split == line.Length - 1) continue;

            var left = line[..split].Trim();
            var right = line[(split + 1)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;

            pairs.Add(new MatchPair { Left = left, Right = right });
        }

        return pairs;
    }

    public static string FormatPairs(IReadOnlyList<MatchPair> pairs)
        => string.Join(Environment.NewLine, pairs.Select(p => $"{p.Left} = {p.Right}"));

    private static IEnumerable<string> Lines(string? text)
        => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
