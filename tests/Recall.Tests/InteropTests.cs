using System.IO;
using Recall.Models;
using Recall.Services;

namespace Recall.Tests;

/// <summary>
/// The parts that talk to the rest of the world: CSV/TSV interop, the distractor
/// filter that keeps multiple choice honest, the leech rule, and the review forecast.
/// </summary>
public class InteropTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "recall-tests-" + Guid.NewGuid().ToString("n"));

    static InteropTests()
    {
        // The store is a singleton, so it has to be pointed at a scratch profile
        // before the first test touches it.
        Environment.SetEnvironmentVariable(
            "RECALL_DATA_DIR",
            Path.Combine(Path.GetTempPath(), "recall-tests-profile"));
    }

    public InteropTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string File(string name) => Path.Combine(_dir, name);

    // ---------- CSV / TSV ----------

    [Fact]
    public void A_deck_survives_a_csv_round_trip_with_commas_and_newlines_intact()
    {
        var cards = new List<Card>
        {
            new() { Front = "What is ATP?", Back = "Adenosine triphosphate, the energy currency", Tags = "bio energy" },
            new() { Front = "Line one", Back = "Line one\nline two", Hint = "two lines" },
            new() { Front = "Quote \"this\"", Back = "A, B; C" }
        };

        var path = File("deck.csv");
        CsvDeck.Export(cards, path);
        var back = CsvDeck.Import(path);

        Assert.Null(back.Error);
        Assert.Equal(3, back.Cards.Count);

        Assert.Equal("What is ATP?", back.Cards[0].Front);
        Assert.Equal("Adenosine triphosphate, the energy currency", back.Cards[0].Back);
        Assert.Equal("bio energy", back.Cards[0].Tags);

        Assert.Equal("Line one\nline two", back.Cards[1].Back);
        Assert.Equal("two lines", back.Cards[1].Hint);

        Assert.Equal("Quote \"this\"", back.Cards[2].Front);
        Assert.Equal("A, B; C", back.Cards[2].Back);
    }

    [Fact]
    public void A_quizlet_export_with_tabs_and_no_header_is_read_as_front_and_back()
    {
        var path = File("quizlet.txt");
        System.IO.File.WriteAllText(path,
            "Mitochondria\tProduces most of the cell's ATP\n" +
            "Ribosome\tBuilds proteins from messenger RNA\n");

        var result = CsvDeck.Import(path);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Cards.Count);
        Assert.Equal("Mitochondria", result.Cards[0].Front);
        Assert.Equal("Produces most of the cell's ATP", result.Cards[0].Back);
    }

    [Fact]
    public void A_header_row_is_recognised_and_named_columns_are_honoured()
    {
        var path = File("anki.csv");
        System.IO.File.WriteAllText(path,
            "Term,Definition,Tags\n" +
            "Osmosis,Movement of water across a membrane,biology\n");

        var result = CsvDeck.Import(path);

        Assert.Null(result.Error);
        var card = Assert.Single(result.Cards);
        Assert.Equal("Osmosis", card.Front);
        Assert.Equal("Movement of water across a membrane", card.Back);
        Assert.Equal("biology", card.Tags);
    }

    [Fact]
    public void Rows_missing_a_side_are_skipped_and_counted()
    {
        var path = File("partial.csv");
        System.IO.File.WriteAllText(path, "front,back\nA,B\nOnly a front\n,C\n");

        var result = CsvDeck.Import(path);

        Assert.Null(result.Error);
        Assert.Single(result.Cards);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public void SafeFileName_strips_path_characters()
    {
        var name = CsvDeck.SafeFileName("Bio: Cells / Chapter 4?");

        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('?', name);
    }

    // ---------- Distractors ----------

    [Fact]
    public void Distractors_never_repeat_the_answer_or_a_spelling_of_it()
    {
        var pool = new[]
        {
            "Photosynthesis", "photosynthesise", "Photosynthesis ", "Cellular respiration",
            "Glycolysis", "Krebs cycle"
        };

        var picked = QuizFactory.PickDistractors("Photosynthesis", pool);

        Assert.DoesNotContain("photosynthesise", picked);
        Assert.DoesNotContain("Photosynthesis ", picked);
        Assert.Equal(3, picked.Count);
    }

    [Fact]
    public void Distractors_drop_boolean_words_and_stubs()
    {
        var pool = new[] { "True", "False", "yes", "no", "x", "", "Mitochondria", "Ribosome", "Nucleus" };

        var picked = QuizFactory.PickDistractors("Golgi apparatus", pool);

        Assert.DoesNotContain("True", picked);
        Assert.DoesNotContain("False", picked);
        Assert.DoesNotContain("yes", picked);
        Assert.DoesNotContain("x", picked);
        Assert.DoesNotContain("", picked);
        Assert.Equal(3, picked.Count);
    }

    [Fact]
    public void Distractors_prefer_answers_of_a_similar_length()
    {
        var pool = new[]
        {
            "ATP",
            "The mitochondrion is the site of aerobic respiration in eukaryotes",
            "Nucleus",
            "Ribosome",
            "Cytoplasm"
        };

        var picked = QuizFactory.PickDistractors("Membrane", pool);

        Assert.Equal(3, picked.Count);
        Assert.DoesNotContain(picked, p => p.Length > 30);
    }

    // ---------- Leech ----------

    [Fact]
    public void A_card_that_lapses_eight_times_is_suspended_once()
    {
        var card = new Card { Front = "Q", Back = "A" };
        var now = new DateTime(2026, 11, 13, 9, 0, 0);

        for (var i = 1; i < Card.SrsLeechThreshold; i++)
            Assert.False(Srs.Apply(card, Grade.Again, now));

        Assert.True(Srs.Apply(card, Grade.Again, now));
        Assert.True(card.Suspended);
        Assert.True(card.IsLeech);

        // The notice fires once, not on every later lapse.
        Assert.False(Srs.Apply(card, Grade.Again, now));
    }

    [Fact]
    public void A_suspended_card_stays_out_of_a_session()
    {
        var live = new Card { Front = "live", Back = "a" };
        var parked = new Card { Front = "parked", Back = "b", Suspended = true };

        var queue = Session.Build(new[] { live, parked }, 50);

        Assert.Single(queue);
        Assert.Equal("live", queue[0].Front);
    }

    [Fact]
    public void A_deck_of_only_suspended_cards_builds_an_empty_session()
    {
        var parked = new Card { Front = "parked", Back = "b", Suspended = true };

        Assert.Empty(Session.Build(new[] { parked }, 50));
    }

    // ---------- Forecast ----------

    [Fact]
    public void The_forecast_buckets_overdue_new_and_future_cards_by_day()
    {
        var store = Store.Instance;
        store.Data.Decks.Clear();

        var today = DateTime.Today;
        var deck = new Deck { Name = "Forecast" };
        deck.Cards.Add(new Card { Front = "overdue", Back = "a", Due = today.AddDays(-3).AddHours(4) });
        deck.Cards.Add(new Card { Front = "new", Back = "b" });
        deck.Cards.Add(new Card { Front = "in two days", Back = "c", Due = today.AddDays(2).AddHours(4) });
        deck.Cards.Add(new Card { Front = "far out", Back = "d", Due = today.AddDays(40).AddHours(4) });
        deck.Cards.Add(new Card { Front = "parked", Back = "e", Due = today, Suspended = true });
        store.Data.Decks.Add(deck);

        var days = store.Forecast(7);

        Assert.Equal(7, days.Count);
        Assert.Equal(today, days[0].Day);

        // Overdue and new cards are today's work; the far-out card is outside the horizon.
        Assert.Equal(2, days[0].Count);
        Assert.Equal(0, days[1].Count);
        Assert.Equal(1, days[2].Count);
        Assert.Equal(0, days[6].Count);
        Assert.Equal(3, days.Sum(d => d.Count));

        store.Data.Decks.Clear();
    }
}
