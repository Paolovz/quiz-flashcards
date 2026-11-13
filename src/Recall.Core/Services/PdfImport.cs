using System.IO;

namespace Recall.Services;

public sealed record PdfExtraction(string FileName, int Pages, List<DraftCard> Cards, string? Error);

public static class PdfImport
{
    /// <summary>
    /// Reads the document as lines with their typography, cleans it once for the whole file
    /// so running headers and footers can be recognised, then extracts cards page by page.
    /// </summary>
    public static PdfExtraction FromFile(string path, int maxCards, IProgress<string>? progress = null)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            var raw = PdfLayout.ReadLines(path, progress);
            var pages = raw.Select(l => l.Page).Distinct().Count();

            if (raw.Count == 0 || raw.Sum(l => l.Text.Length) < 80)
                return new PdfExtraction(name, pages, new List<DraftCard>(),
                    "No selectable text found. Scanned PDFs need OCR first.");

            progress?.Report("Extracting cards…");

            var prepared = CardFactory.PrepareDocument(raw);
            var readable = prepared
                .GroupBy(l => l.Page)
                .Where(g => g.Sum(l => l.Text.Length) > 80)
                .OrderBy(g => g.Key)
                .ToList();

            if (readable.Count == 0)
                return new PdfExtraction(name, pages, new List<DraftCard>(),
                    "The pages are mostly headings and captions, so there was nothing to make cards from.");

            var perPage = Math.Max(2, (int)Math.Ceiling(maxCards / (double)readable.Count) * 2);
            var drafts = new List<DraftCard>();

            foreach (var page in readable)
                drafts.AddRange(CardFactory.FromLines(page.ToList(), perPage, $"p. {page.Key}"));

            var ranked = drafts
                .OrderByDescending(d => d.Score)
                .ThenBy(d => d.Source)
                .ThenBy(d => d.Order)
                .Take(maxCards)
                .ToList();

            return new PdfExtraction(name, pages, ranked, null);
        }
        catch (Exception ex)
        {
            return new PdfExtraction(name, 0, new List<DraftCard>(), ex.Message);
        }
    }
}
