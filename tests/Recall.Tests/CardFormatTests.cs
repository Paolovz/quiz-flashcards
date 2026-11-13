using Recall.Models;
using Recall.Services;

namespace Recall.Tests;

public class CardFormatTests
{
    [Fact]
    public void Choices_keep_their_order_and_mark_the_correct_ones()
    {
        var (choices, correct) = CardFormat.ParseChoices("one\n*two\nthree\n*four");

        Assert.Equal(new[] { "one", "two", "three", "four" }, choices);
        Assert.Equal(new[] { 1, 3 }, correct);
    }

    [Fact]
    public void Blank_lines_and_stray_stars_are_ignored()
    {
        var (choices, correct) = CardFormat.ParseChoices("  *only\n\n   \n*");

        Assert.Single(choices);
        Assert.Equal("only", choices[0]);
        Assert.Equal(new[] { 0 }, correct);
    }

    [Fact]
    public void Choices_round_trip()
    {
        var text = CardFormat.FormatChoices(new[] { "a", "b", "c" }, new[] { 0, 2 });
        var (choices, correct) = CardFormat.ParseChoices(text);

        Assert.Equal(new[] { "a", "b", "c" }, choices);
        Assert.Equal(new[] { 0, 2 }, correct);
    }

    [Fact]
    public void Items_are_read_top_to_bottom()
    {
        Assert.Equal(new[] { "first", "second", "third" }, CardFormat.ParseItems("first\nsecond\nthird"));
    }

    [Fact]
    public void Pairs_split_on_the_first_equals_sign()
    {
        var pairs = CardFormat.ParsePairs("H2O = water\nNaCl = salt");

        Assert.Equal(2, pairs.Count);
        Assert.Equal("H2O", pairs[0].Left);
        Assert.Equal("water", pairs[0].Right);
    }

    [Fact]
    public void Pairs_without_a_right_side_are_dropped()
    {
        var pairs = CardFormat.ParsePairs("lonely =\n= nameless\nok = fine");

        Assert.Single(pairs);
        Assert.Equal("ok", pairs[0].Left);
    }

    [Fact]
    public void Pairs_round_trip()
    {
        var text = CardFormat.FormatPairs(new[]
        {
            new MatchPair { Left = "a", Right = "1" },
            new MatchPair { Left = "b", Right = "2" }
        });

        var pairs = CardFormat.ParsePairs(text);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("b", pairs[1].Left);
        Assert.Equal("2", pairs[1].Right);
    }
}

public class CardKindQuizTests
{
    private static QuizQuestion Build(Card card) => QuizFactory.BuildOne(card, Array.Empty<string>());

    [Theory]
    [InlineData("True", 0)]
    [InlineData("true", 0)]
    [InlineData("False", 1)]
    public void True_or_false_offers_both_options(string answer, int correct)
    {
        var q = Build(new Card { Kind = CardKind.TrueFalse, Front = "Water boils at 100C", Back = answer });

        Assert.Equal(new[] { "True", "False" }, q.Options);
        Assert.Equal(correct, q.CorrectIndex);
    }

    [Fact]
    public void Multi_select_keeps_every_correct_index()
    {
        var q = Build(new Card
        {
            Kind = CardKind.MultiSelect,
            Front = "Which are noble gases?",
            Choices = new List<string> { "Helium", "Oxygen", "Neon", "Iron" },
            CorrectChoices = new List<int> { 0, 2 }
        });

        Assert.Equal(4, q.Options.Length);
        // The row is shuffled, so the key travels with the text, not the index.
        Assert.Equal(
            new[] { "Helium", "Neon" },
            q.CorrectIndexes.Select(i => q.Options[i]).OrderBy(x => x));
        Assert.False(q.IsTyped);
    }

    [Fact]
    public void Fill_the_blanks_is_typed_and_hides_the_answer()
    {
        var q = Build(new Card
        {
            Kind = CardKind.FillBlanks,
            Front = "The {{mitochondria}} produces ATP",
            Back = "mitochondria"
        });

        Assert.False(q.IsChoice);
        Assert.True(q.IsTyped);
        Assert.DoesNotContain("mitochondria", q.Prompt);
        Assert.Contains("______", q.Prompt);
    }

    [Fact]
    public void Order_shuffles_the_items()
    {
        var card = new Card
        {
            Kind = CardKind.Order,
            Front = "Sort by size",
            Items = new List<string> { "atom", "molecule", "cell" }
        };

        var q = Build(card);

        Assert.Equal(3, q.Shuffled.Length);
        Assert.Equal(card.Items.OrderBy(x => x), q.Shuffled.OrderBy(x => x));
    }

    [Fact]
    public void Match_shuffles_the_right_hand_column()
    {
        var card = new Card
        {
            Kind = CardKind.Match,
            Front = "Match the symbol",
            Pairs = new List<MatchPair>
            {
                new() { Left = "H", Right = "Hydrogen" },
                new() { Left = "O", Right = "Oxygen" }
            }
        };

        var q = Build(card);

        Assert.Equal(2, q.Rights.Length);
        Assert.Contains("Oxygen", q.Rights);
        Assert.Equal(2, q.Card.Pairs.Count);
    }

    [Fact]
    public void A_deck_quiz_handles_every_kind()
    {
        var deck = new Deck { Name = "Mixed" };
        deck.Cards.Add(new Card { Front = "plain", Back = "answer" });
        deck.Cards.Add(new Card { Kind = CardKind.TrueFalse, Front = "sky is blue", Back = "True" });
        deck.Cards.Add(new Card
        {
            Kind = CardKind.MultiSelect,
            Front = "pick two",
            Choices = new List<string> { "a", "b", "c" },
            CorrectChoices = new List<int> { 0, 1 }
        });

        var questions = QuizFactory.Build(deck, deck.Cards);

        Assert.Equal(3, questions.Count);
        // Order is shuffled; assert every kind is present exactly once.
        Assert.Equal(
            new[] { CardKind.Qa, CardKind.TrueFalse, CardKind.MultiSelect }.OrderBy(k => k),
            questions.Select(q => q.Kind).OrderBy(k => k));
    }
}
