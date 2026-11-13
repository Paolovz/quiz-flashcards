using Recall.Services;

namespace Recall.Tests;

public class TextMatchTests
{
    [Theory]
    [InlineData("Mitochondria")]
    [InlineData("mitochondria")]
    [InlineData("  Mitochondria.  ")]
    [InlineData("MITOCHONDRIA")]
    public void Case_and_punctuation_do_not_matter(string typed)
    {
        Assert.True(TextMatch.Matches(typed, new[] { "Mitochondria" }));
    }

    [Theory]
    [InlineData("mitocondria")]     // missing letter
    [InlineData("mitochondira")]    // transposed letters
    [InlineData("mitochondiraa")]   // extra letter
    public void Misspelled_answers_fail(string typed)
    {
        Assert.False(TextMatch.Matches(typed, new[] { "Mitochondria" }));
    }

    [Fact]
    public void A_different_answer_fails()
    {
        Assert.False(TextMatch.Matches("Ribosome", new[] { "Mitochondria" }));
    }

    [Fact]
    public void Diacritics_are_ignored()
    {
        Assert.True(TextMatch.Matches("el nino", new[] { "El Niño" }));
    }

    [Fact]
    public void Any_accepted_alternative_passes()
    {
        Assert.True(TextMatch.Matches("plasma membrane", new[] { "Cell membrane", "plasma membrane" }));
    }

    [Fact]
    public void Leading_articles_are_ignored()
    {
        Assert.True(TextMatch.Matches("the nucleus", new[] { "nucleus" }));
    }

    [Theory]
    [InlineData("Mitochondria, the site of ATP synthesis")]
    [InlineData("Mitochondria — site of ATP synthesis")]
    [InlineData("Mitochondria (site of ATP synthesis)")]
    [InlineData("Mitochondria - site of ATP synthesis")]
    public void The_key_term_alone_answers_a_longer_card(string stored)
    {
        Assert.True(TextMatch.Matches("mitochondria", new[] { stored }));
    }

    [Fact]
    public void The_key_term_alone_survives_a_leading_article()
    {
        Assert.True(TextMatch.Matches("mitochondria", new[] { "The Mitochondria, site of ATP synthesis" }));
    }

    [Fact]
    public void Typing_the_key_term_plus_your_own_detail_passes()
    {
        Assert.True(TextMatch.Matches(
            "mitochondria powerhouse of the cell",
            new[] { "Mitochondria, the site of ATP synthesis" }));
    }

    [Fact]
    public void A_part_of_a_longer_name_is_not_the_answer()
    {
        Assert.False(TextMatch.Matches("cell", new[] { "Cell membrane" }));
        Assert.False(TextMatch.Matches("membrane", new[] { "Cell membrane" }));
    }

    [Fact]
    public void Empty_input_fails()
    {
        Assert.False(TextMatch.Matches("", new[] { "Mitochondria" }));
        Assert.False(TextMatch.Matches("   ", new[] { "Mitochondria" }));
        Assert.False(TextMatch.Matches(null, new[] { "Mitochondria" }));
    }

    [Fact]
    public void Short_answers_do_not_match_on_a_single_shared_letter_run()
    {
        Assert.False(TextMatch.Matches("cat", new[] { "dog" }));
    }
}
