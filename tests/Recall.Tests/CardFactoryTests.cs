using Recall.Models;
using Recall.Services;

namespace Recall.Tests;

public class CardFactoryTests
{
    private const string TextbookPage = """
        Cell Structure and Function

        Biology 101 - Chapter 4

        Mitochondria are the organelles that produce most of the cell's ATP through
        oxidative phosphorylation. The cell membrane is a selectively permeable barrier
        that controls which molecules enter and leave the cell. Ribosomes are the sites
        of protein synthesis, either free in the cytoplasm or attached to the rough
        endoplasmic reticulum.

        Figure 4.1: A typical eukaryotic cell.

        The Golgi apparatus packages proteins into vesicles for transport. Lysosomes
        contain hydrolytic enzymes that digest worn-out organelles and foreign material.

        12
        """;

    [Fact]
    public void Definition_sentences_become_cards()
    {
        var cards = CardFactory.FromText(TextbookPage, 20);

        Assert.Contains(cards, c => c.Front == "Mitochondria");
        Assert.Contains(cards, c => c.Front == "cell membrane");
        Assert.Contains(cards, c => c.Front == "Ribosomes");
    }

    [Fact]
    public void Sentences_without_a_copula_fall_back_to_cloze_cards()
    {
        var cards = CardFactory.FromText(TextbookPage, 20);

        Assert.Contains(cards, c => c.Kind == "cloze" && c.Back.Contains("Golgi"));
        Assert.Contains(cards, c => c.Kind == "cloze" && c.Back.Contains("Lysosome"));
        Assert.All(cards.Where(c => c.Kind == "cloze"), c => Assert.Contains("____", c.Front));
    }

    [Fact]
    public void Definition_back_is_quoted_from_the_source()
    {
        var cards = CardFactory.FromText(TextbookPage, 20);
        var mitochondria = Assert.Single(cards, c => c.Front == "Mitochondria");

        Assert.Contains("produce most of the cell's ATP", mitochondria.Back);
        Assert.StartsWith("The organelles", mitochondria.Back);
    }

    [Fact]
    public void Page_numbers_and_captions_never_become_cards()
    {
        var cards = CardFactory.FromText(TextbookPage, 20);

        Assert.DoesNotContain(cards, c => c.Front.Contains("Figure"));
        Assert.DoesNotContain(cards, c => c.Front.Trim() == "12");
        Assert.DoesNotContain(cards, c => c.Front.Contains("Biology 101"));
    }

    [Fact]
    public void Card_count_respects_the_limit()
    {
        var cards = CardFactory.FromText(TextbookPage, 2);
        Assert.True(cards.Count <= 2, $"got {cards.Count}");
    }

    [Fact]
    public void Cloze_cards_are_generated_when_there_are_no_definitions()
    {
        const string prose = """
            The process of photosynthesis converts light energy into glucose using carbon
            dioxide and water. Chloroplasts inside the leaf absorb the incoming photons
            and drive the light dependent reactions of the pathway.
            """;

        var cards = CardFactory.FromText(prose, 5);

        Assert.NotEmpty(cards);
        Assert.All(cards, c => Assert.Contains("____", c.Front));
        Assert.All(cards, c => Assert.Equal("cloze", c.Kind));
    }

    [Fact]
    public void Duplicate_fronts_are_dropped()
    {
        const string repeated = """
            Osmosis is the movement of water across a semi permeable membrane. Osmosis is
            the movement of water across a semi permeable membrane. Diffusion is the
            movement of particles from high to low concentration.
            """;

        var cards = CardFactory.FromText(repeated, 10);
        var fronts = cards.Select(c => c.Front.ToLowerInvariant()).ToList();

        Assert.Equal(fronts.Count, fronts.Distinct().Count());
    }

    [Fact]
    public void Empty_or_whitespace_input_returns_nothing()
    {
        Assert.Empty(CardFactory.FromText("", 10));
        Assert.Empty(CardFactory.FromText("   \n\n  ", 10));
        Assert.Empty(CardFactory.FromText(null, 10));
        Assert.Empty(CardFactory.FromText("Some text.", 0));
    }

    [Fact]
    public void Source_page_is_recorded_on_every_card()
    {
        var cards = CardFactory.FromText(TextbookPage, 5, "p. 12");

        Assert.NotEmpty(cards);
        Assert.All(cards, c => Assert.Equal("p. 12", c.Source));
    }

    [Fact]
    public void Hyphenated_line_breaks_are_rejoined()
    {
        const string broken = """
            The mitochondrion is the organelle respon-
            sible for producing ATP inside the cell.
            """;

        var cards = CardFactory.FromText(broken, 5);

        Assert.Contains(cards, c => c.Back.Contains("responsible"));
    }

    [Fact]
    public void Headings_do_not_leak_into_the_term()
    {
        var cards = CardFactory.FromText(TextbookPage, 20);

        Assert.DoesNotContain(cards, c => c.Front.Contains("Chapter"));
        Assert.DoesNotContain(cards, c => c.Front.Contains("Cell Structure"));
    }
}
