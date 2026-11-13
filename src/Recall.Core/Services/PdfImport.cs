using System.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Recall.Services;

public sealed record PdfExtraction(string FileName, int Pages, List<DraftCard> Cards, string? Error);

public static class PdfImport
{
    /// Extracts text per page, runs the card factory over each page, then keeps the
    /// highest scoring drafts across the whole document.
    public static PdfExtraction FromFile(string path, int maxCards, IProgress<string>? progress = null)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            using var document = PdfDocument.Open(path);
            var pages = new List<(int Number, string Text)>();

            foreach (var page in document.GetPages())
            {
                progress?.Report($"Reading page {page.Number}…");
                string text;
                try
                {
                    text = ContentOrderTextExtractor.GetText(page);
                }
                catch (Exception)
                {
                    text = page.Text; // layout analysis can fail on unusual content streams
                }
                pages.Add((page.Number, text ?? ""));
            }

            progress?.Report("Extracting cards…");

            var readable = pages.Where(p => p.Text.Trim().Length > 80).ToList();
            if (readable.Count == 0)
                return new PdfExtraction(name, pages.Count, new List<DraftCard>(), "No selectable text found. Scanned PDFs need OCR first.");

            var perPage = Math.Max(2, (int)Math.Ceiling(maxCards / (double)readable.Count) * 2);
            var drafts = new List<DraftCard>();

            foreach (var (number, text) in readable)
                drafts.AddRange(CardFactory.FromText(text, perPage, $"p. {number}"));

            var ranked = drafts
                .OrderByDescending(d => d.Score)
                .ThenBy(d => d.Source)
                .ThenBy(d => d.Order)
                .Take(maxCards)
                .ToList();

            return new PdfExtraction(name, pages.Count, ranked, null);
        }
        catch (Exception ex)
        {
            return new PdfExtraction(name, 0, new List<DraftCard>(), ex.Message);
        }
    }
}
