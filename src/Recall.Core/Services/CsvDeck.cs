using System.IO;
using System.Text;
using Recall.Models;

namespace Recall.Services;

public sealed record CsvImport(List<Card> Cards, int Skipped, string? Error);

/// <summary>
/// Reads and writes the tab/csv shape every other flashcard app speaks. Anki exports
/// "front,back,tags", Brainscape and Quizlet export two columns, and both are accepted
/// here, so a deck made elsewhere comes in without retyping it.
/// </summary>
public static class CsvDeck
{
    private static readonly string[] HeaderNames =
    {
        "front", "question", "term", "prompt", "word", "key"
    };

    /// <summary>
    /// One row per card: front, back, kind, hint, tags. Quoted per RFC 4180 so a comma
    /// or a newline inside a card survives the round trip.
    /// </summary>
    public static void Export(IEnumerable<Card> cards, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("front,back,kind,hint,tags");

        foreach (var card in cards)
        {
            sb.Append(Quote(card.Front)).Append(',')
              .Append(Quote(card.Back)).Append(',')
              .Append(Quote(card.Kind.ToString())).Append(',')
              .Append(Quote(card.Hint ?? "")).Append(',')
              .Append(Quote(string.Join(' ', card.TagList)))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>
    /// Parses a delimited file into Q&amp;A cards. The delimiter is detected from the
    /// header line, a header row is optional, and a "kind" column is honoured when the
    /// file came from this app.
    /// </summary>
    public static CsvImport Import(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return new CsvImport(new List<Card>(), 0, "The file is empty.");

            var rows = Parse(text, out var delimiter);
            if (rows.Count == 0)
                return new CsvImport(new List<Card>(), 0, "No rows found in that file.");

            var hasHeader = LooksLikeHeader(rows[0], delimiter);
            var start = hasHeader ? 1 : 0;

            var columns = hasHeader
                ? IndexColumns(rows[0], delimiter)
                : new Dictionary<string, int> { ["front"] = 0, ["back"] = 1 };

            if (!columns.ContainsKey("back"))
                return new CsvImport(new List<Card>(), 0,
                    $"Expected two columns, found {rows[0].Count} on the first line.");

            var cards = new List<Card>();
            var skipped = 0;

            for (var i = start; i < rows.Count; i++)
            {
                var row = rows[i];
                var front = Cell(row, columns, "front");
                var back = Cell(row, columns, "back");

                if (front.Length == 0 || back.Length == 0)
                {
                    if (row.Any(c => c.Trim().Length > 0)) skipped++;
                    continue;
                }

                var card = new Card
                {
                    Front = front,
                    Back = back,
                    Hint = NullIfBlank(Cell(row, columns, "hint")),
                    Tags = NullIfBlank(Cell(row, columns, "tags"))
                };

                if (Enum.TryParse<CardKind>(Cell(row, columns, "kind"), true, out var kind))
                    card.Kind = kind;

                cards.Add(card);
            }

            return new CsvImport(cards, skipped, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CsvImport(new List<Card>(), 0, ex.Message);
        }
    }

    private static bool LooksLikeHeader(List<string> row, char delimiter)
    {
        if (row.Count == 0) return false;

        var first = row[0].Trim().Trim('"').ToLowerInvariant();
        if (!HeaderNames.Contains(first)) return false;

        // A card whose front is literally "term" would still have a comma in it, so a
        // single-column first row is a header only when a known name is alone.
        return row.Count > 1 || delimiter != '\t';
    }

    private static Dictionary<string, int> IndexColumns(List<string> header, char delimiter)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim().Trim('"').ToLowerInvariant();

            if (HeaderNames.Contains(name) && !map.ContainsKey("front")) map["front"] = i;
            else if (name is "back" or "definition" or "answer" or "meaning" && !map.ContainsKey("back")) map["back"] = i;
            else if (name is "kind" or "type") map["kind"] = i;
            else if (name is "hint" or "clue") map["hint"] = i;
            else if (name is "tags" or "tag" or "deck") map["tags"] = i;
        }

        // A two-column file with unrecognised headers is still two columns.
        if (!map.ContainsKey("front") && header.Count >= 1) map["front"] = 0;
        if (!map.ContainsKey("back") && header.Count >= 2) map["back"] = 1;

        return map;
    }

    private static string Cell(List<string> row, Dictionary<string, int> columns, string name)
        => columns.TryGetValue(name, out var index) && index < row.Count ? row[index].Trim() : "";

    private static string? NullIfBlank(string value) => value.Length == 0 ? null : value;

    private static string Quote(string value)
    {
        if (value.Length == 0) return "";
        var needs = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needs) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>RFC 4180 reader: quoted cells may hold the delimiter, quotes and newlines.</summary>
    private static List<List<string>> Parse(string text, out char delimiter)
    {
        delimiter = DetectDelimiter(text);

        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else quoted = false;
                }
                else cell.Append(c);
                continue;
            }

            if (c == '"' && cell.Length == 0) { quoted = true; continue; }
            if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); continue; }

            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString());
                cell.Clear();
                if (row.Any(v => v.Trim().Length > 0)) rows.Add(row);
                row = new List<string>();
                continue;
            }

            cell.Append(c);
        }

        row.Add(cell.ToString());
        if (row.Any(v => v.Trim().Length > 0)) rows.Add(row);

        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        var line = text.Split('\n', 2)[0];
        var counts = new (char Delimiter, int Count)[]
        {
            (',', line.Count(c => c == ',')),
            ('\t', line.Count(c => c == '\t')),
            (';', line.Count(c => c == ';'))
        };

        var best = counts.OrderByDescending(c => c.Count).First();
        return best.Count > 0 ? best.Delimiter : '\t';
    }

    /// <summary>Filename-safe version of a deck name, used as the default export name.</summary>
    public static string SafeFileName(string name)
    {
        var safe = new StringBuilder(name.Length);
        var invalid = Path.GetInvalidFileNameChars();

        foreach (var c in name)
            safe.Append(invalid.Contains(c) ? '-' : c);

        var trimmed = safe.ToString().Trim();
        return trimmed.Length == 0 ? "deck" : trimmed;
    }
}
