using System.Windows;
using Recall.Services;

namespace Recall.Theming;

/// <summary>One selectable palette. Swatches are literal hex so the picker can show a
/// theme it is not currently wearing.</summary>
public sealed record ThemeOption(
    string Id,
    string Name,
    string Blurb,
    bool IsDark,
    string Background,
    string Surface,
    string Primary,
    string Accent);

/// <summary>Swaps the palette dictionary at runtime. Views reference brushes with
/// DynamicResource, so replacing merged dictionary 0 repaints every live element.</summary>
public static class ThemeManager
{
    private const int PaletteIndex = 0;

    public static IReadOnlyList<ThemeOption> All { get; } = new[]
    {
        new ThemeOption("clay", "Clay", "Study purple on paper. The original.",
            false, "#FAF5FF", "#FFFFFF", "#7C3AED", "#059669"),
        new ThemeOption("deepocean", "Deep Ocean", "Near-black navy with a teal current.",
            true, "#071722", "#0E2634", "#2DD4BF", "#34D399"),
        new ThemeOption("sandstone", "Sandstone", "Warm paper, burnt orange, calm teal.",
            false, "#FDF8F3", "#FFFFFF", "#C2410C", "#0F766E"),
        new ThemeOption("forest", "Forest Night", "Dark pine with a bright leaf green.",
            true, "#0B1512", "#12221C", "#4ADE80", "#5EEAD4")
    };

    public static string Current { get; private set; } = "clay";

    public static ThemeOption CurrentOption =>
        All.FirstOrDefault(t => t.Id == Current) ?? All[0];

    /// Raised after the palette dictionary has been replaced.
    public static event Action? Changed;

    public static void Apply(string? id, bool persist = true)
    {
        var option = All.FirstOrDefault(t => t.Id == id) ?? All[0];
        if (option.Id != Current) SwapDictionary(option.Id);

        Current = option.Id;

        if (persist)
        {
            Store.Instance.Data.Theme = option.Id;
            Store.Instance.Save();
        }

        Changed?.Invoke();
    }

    private static void SwapDictionary(string id)
    {
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count <= PaletteIndex) return;

        var source = new Uri($"Themes/Palette/{FileName(id)}.xaml", UriKind.Relative);
        merged[PaletteIndex] = new ResourceDictionary { Source = source };
    }

    private static string FileName(string id) => id switch
    {
        "deepocean" => "DeepOcean",
        "sandstone" => "Sandstone",
        "forest" => "Forest",
        _ => "Clay"
    };
}
