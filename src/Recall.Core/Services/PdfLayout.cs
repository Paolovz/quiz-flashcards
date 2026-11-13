using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Recall.Services;

/// <summary>
/// One visual line of a PDF, with the typography that produced it. <paramref name="Left"/>
/// and <paramref name="Right"/> are the glyph extents, which is how a wrapped line is told
/// apart from a short line that simply ended: only a wrapped line reaches the right margin.
/// </summary>
public sealed record PdfLine(
    int Page,
    string Text,
    double Size,
    double BoldRatio,
    double Left,
    double Right = 0,
    bool ListItem = false)
{
    public bool IsBold => BoldRatio >= 0.6;

    /// <summary>Zero when the line did not come from a laid-out page (plain text import).</summary>
    public double Width => Right > Left ? Right - Left : 0;
}

/// <summary>
/// Pulls lines out of a PDF while keeping the two signals that decide whether a line is a
/// term, a heading, or body prose: font size and how much of the line is bold.
/// </summary>
public static class PdfLayout
{
    private static readonly string[] BoldMarkers =
    {
        "bold", "black", "heavy", "semibold", "semi-bold", "demibold", "demi-bold", "extrabold", "-bd", ",b"
    };

    public static List<PdfLine> ReadLines(string path, IProgress<string>? progress = null)
    {
        var lines = new List<PdfLine>();

        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            progress?.Report($"Reading page {page.Number}…");
            lines.AddRange(ReadPage(page.Number, page));
        }

        return lines;
    }

    private static IEnumerable<PdfLine> ReadPage(int number, Page page)
    {
        var buckets = new SortedDictionary<int, List<Letter>>(Comparer<int>.Create((a, b) => b.CompareTo(a)));

        foreach (var letter in page.Letters)
        {
            if (string.IsNullOrEmpty(letter.Value)) continue;

            // Group by baseline, rounded to the nearest point. A superscript or a slightly
            // shifted glyph still lands on the line it belongs to.
            var key = (int)Math.Round(letter.StartBaseLine.Y);
            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = new List<Letter>();
            bucket.Add(letter);
        }

        foreach (var bucket in buckets.Values)
        {
            var ordered = bucket.OrderBy(l => l.StartBaseLine.X).ToList();
            var text = Join(ordered);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var bold = ordered.Count(l => IsBoldFont(l.FontName));
            var size = ordered
                .GroupBy(l => Math.Round(l.FontSize, 1))
                .OrderByDescending(g => g.Count())
                .First().Key;

            yield return new PdfLine(
                number,
                text,
                size,
                bold / (double)ordered.Count,
                ordered[0].StartBaseLine.X,
                ordered[^1].EndBaseLine.X);
        }
    }

    /// <summary>Letters carry no spaces, so a gap wider than a fraction of the size is a space.</summary>
    private static string Join(List<Letter> letters)
    {
        var sb = new StringBuilder();
        Letter? previous = null;

        foreach (var letter in letters)
        {
            if (previous is not null)
            {
                var gap = letter.StartBaseLine.X - previous.EndBaseLine.X;
                var threshold = Math.Max(previous.FontSize, letter.FontSize) * 0.18;
                if (gap > threshold) sb.Append(' ');
            }

            sb.Append(letter.Value);
            previous = letter;
        }

        return SpaceRun(sb.ToString());
    }

    private static string SpaceRun(string value)
    {
        var sb = new StringBuilder(value.Length);
        var space = false;
        foreach (var ch in value)
        {
            if (ch is ' ' or '\t' or '\u00a0')
            {
                if (!space) sb.Append(' ');
                space = true;
                continue;
            }

            space = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    public static bool IsBoldFont(string? fontName)
    {
        if (string.IsNullOrEmpty(fontName)) return false;
        var lower = fontName.ToLowerInvariant();
        foreach (var marker in BoldMarkers)
            if (lower.Contains(marker, StringComparison.Ordinal))
                return true;
        return false;
    }
}
