using Recall.Models;
using Recall.Services;

namespace Recall.Tests;

public class QuizFactoryTests
{
    private static Deck DeckWith(params (string Front, string Back)[] cards)
    {
        var deck = new Deck { Name = "Test" };
        foreach (var (front, back) in cards)
            deck.Cards.Add(new Card { Front = front, Back = back });
        return deck;
    }

    [Fact]
    public void Short_answers_are_typed_when_the_deck_is_large_enough()
    {
        var deck = DeckWith(
            ("hablar — yo", "hablo"), ("comer — tú", "comes"), ("vivir — él", "vive"),
            ("tener — nosotros", "tenemos"), ("ir — yo", "voy"));

        var questions = QuizFactory.Build(deck, deck.Cards);

        Assert.All(questions, q => Assert.False(q.IsChoice));
    }

    [Fact]
    public void Long_answers_become_multiple_choice_with_one_correct_option()
    {
        var deck = DeckWith(
            ("What produces ATP?", "Mitochondria, the powerhouse of the cell"),
            ("What makes proteins?", "Ribosomes, which read messenger RNA"),
            ("What stores DNA?", "The nucleus, wrapped around histones"),
            ("What digests waste?", "Lysosomes, filled with enzymes"));

        var questions = QuizFactory.Build(deck, deck.Cards);

        Assert.All(questions, q =>
        {
            Assert.True(q.IsChoice);
            Assert.Equal(4, q.Options.Length);
            Assert.InRange(q.CorrectIndex, 0, 3);
            Assert.Equal(1, q.Options.Count(o => o == q.Card.Answer));
        });
    }

    [Fact]
    public void Options_are_unique()
    {
        var deck = DeckWith(
            ("a", "one long answer here"),
            ("b", "another long answer here"),
            ("c", "a third long answer here"),
            ("d", "a fourth long answer here"));

        var questions = QuizFactory.Build(deck, deck.Cards);

        Assert.All(questions, q => Assert.Equal(q.Options.Length, q.Options.Distinct().Count()));
    }

    [Fact]
    public void A_two_card_deck_still_produces_usable_questions()
    {
        var deck = DeckWith(
            ("a", "one long answer here"),
            ("b", "another long answer here"));

        var questions = QuizFactory.Build(deck, deck.Cards);

        Assert.Equal(2, questions.Count);
        Assert.All(questions, q => Assert.True(q.IsChoice));
        Assert.All(questions, q => Assert.Equal(2, q.Options.Length));
    }

    [Fact]
    public void Every_card_gets_a_question()
    {
        var deck = DeckWith(
            ("a", "alpha"), ("b", "beta"), ("c", "gamma"), ("d", "delta"), ("e", "epsilon"));

        var questions = QuizFactory.Build(deck, deck.Cards);

        Assert.Equal(5, questions.Count);
        // Question order is shuffled, so compare the set of cards asked.
        Assert.Equal(
            deck.Cards.Select(c => c.Id).OrderBy(id => id),
            questions.Select(q => q.Card.Id).OrderBy(id => id));
    }

    [Fact]
    public void Multiple_choice_keeps_its_single_key_after_the_row_is_shuffled()
    {
        var card = new Card
        {
            Kind = CardKind.MultipleChoice,
            Front = "Which one?",
            Choices = new List<string> { "alpha", "beta", "gamma", "delta" },
            CorrectChoices = new List<int> { 2 }
        };

        // Shuffling must never lose the answer, so run it enough times to catch a slip.
        for (var run = 0; run < 200; run++)
        {
            var question = QuizFactory.BuildOne(card, Array.Empty<string>());

            Assert.Equal(4, question.Options.Length);
            Assert.InRange(question.CorrectIndex, 0, 3);
            Assert.Equal("gamma", question.Options[question.CorrectIndex]);
            Assert.Single(question.Options, o => o == "gamma");
        }
    }

    [Fact]
    public void Multi_select_keeps_every_key_after_the_row_is_shuffled()
    {
        var card = new Card
        {
            Kind = CardKind.MultiSelect,
            Front = "Which ones?",
            Choices = new List<string> { "alpha", "beta", "gamma", "delta" },
            CorrectChoices = new List<int> { 0, 3 }
        };

        for (var run = 0; run < 200; run++)
        {
            var question = QuizFactory.BuildOne(card, Array.Empty<string>());

            var picked = question.CorrectIndexes.Select(i => question.Options[i]).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "alpha", "delta" }, picked);
        }
    }

    [Fact]
    public void Question_order_is_shuffled()
    {
        var deck = DeckWith(
            ("a", "alpha"), ("b", "beta"), ("c", "gamma"),
            ("d", "delta"), ("e", "epsilon"), ("f", "zeta"), ("g", "eta"), ("h", "theta"));

        var orders = Enumerable.Range(0, 20)
            .Select(_ => string.Join(",", QuizFactory.Build(deck, deck.Cards).Select(q => q.Card.Front)))
            .Distinct()
            .Count();

        // A fixed order would produce exactly one string across twenty builds.
        Assert.True(orders > 1, "question order never changed across 20 builds");
    }

    [Fact]
    public void An_order_question_never_opens_in_the_correct_sequence()
    {
        var card = new Card
        {
            Kind = CardKind.Order,
            Front = "Sort these from smallest to largest",
            Items = new List<string> { "Atom", "Molecule", "Organelle", "Cell" }
        };

        // The quiz refuses to grade an untouched row, so this invariant has to hold:
        // a row that has not been moved is never already the answer.
        for (var run = 0; run < 200; run++)
        {
            var question = QuizFactory.BuildOne(card, Array.Empty<string>());

            Assert.Equal(card.Items.OrderBy(x => x), question.Shuffled.OrderBy(x => x));
            Assert.False(question.Shuffled.SequenceEqual(card.Items),
                "an order question opened with the answer already in place");
        }
    }
}

public class SessionTests
{
    private static Card Due(int daysAgo) => new()
    {
        Front = "f",
        Back = "b",
        Attempts = 1,
        Due = DateTime.Now.AddDays(-daysAgo)
    };

    [Fact]
    public void Due_cards_come_first_oldest_due_date_first()
    {
        var older = Due(5);
        var newer = Due(1);
        var fresh = new Card { Front = "new", Back = "b" };

        var queue = Session.Build(new List<Card> { fresh, newer, older }, 10);

        Assert.Equal(3, queue.Count);
        Assert.Same(older, queue[0]);
        Assert.Same(newer, queue[1]);
        Assert.Same(fresh, queue[2]);
    }

    [Fact]
    public void The_limit_is_respected()
    {
        var cards = Enumerable.Range(0, 20).Select(i => new Card { Front = $"f{i}", Back = "b" }).ToList();
        Assert.Equal(5, Session.Build(cards, 5).Count);
    }

    [Fact]
    public void Nothing_due_falls_back_to_a_cram_session()
    {
        var cards = new List<Card>
        {
            new() { Front = "a", Back = "b", Attempts = 1, Due = DateTime.Now.AddDays(10) },
            new() { Front = "c", Back = "d", Attempts = 1, Due = DateTime.Now.AddDays(20) }
        };

        Assert.Equal(2, Session.Build(cards, 10).Count);
    }

    [Fact]
    public void A_zero_limit_still_returns_one_card()
    {
        var cards = new List<Card> { new() { Front = "a", Back = "b" } };
        Assert.Single(Session.Build(cards, 0));
    }
}
