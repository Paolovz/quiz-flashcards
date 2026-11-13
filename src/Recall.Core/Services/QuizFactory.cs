using Recall.Models;

namespace Recall.Services;

public sealed class QuizQuestion
{
    public Card Card { get; init; } = new();
    public string Prompt { get; init; } = "";
    public string[] Options { get; init; } = Array.Empty<string>();
    public int CorrectIndex { get; init; } = -1;
    public int[] CorrectIndexes { get; init; } = Array.Empty<int>();

    /// Order questions: the items in a shuffled display sequence.
    public string[] Shuffled { get; init; } = Array.Empty<string>();

    /// Match questions: the right-hand column, shuffled. Left side is Pairs order.
    public string[] Rights { get; init; } = Array.Empty<string>();

    public CardKind Kind => Card.Kind;
    public bool IsChoice => Options.Length >= 2;
    public bool IsTyped => Options.Length < 2 && Kind is CardKind.Qa or CardKind.Image or CardKind.FillBlanks;
}

public static class QuizFactory
{
    /// Builds one question per card, shaped by the card kind. Q&A and image cards fall
    /// back to multiple choice when the deck has enough distinct answers to distract with.
    /// The question order is shuffled so a repeat session never replays the last one.
    public static List<QuizQuestion> Build(Deck deck, IReadOnlyList<Card> cards)
    {
        var answers = deck.Cards
            .Where(c => !string.IsNullOrWhiteSpace(c.Answer))
            .Select(c => c.Answer.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var questions = new List<QuizQuestion>(cards.Count);

        foreach (var card in cards)
            questions.Add(BuildOne(card, answers));

        return Shuffle(questions);
    }

    /// Fisher-Yates over a copy. Every consumer that presents options uses this so the
    /// correct answer never sits in the same slot twice.
    public static List<T> Shuffle<T>(IReadOnlyList<T> source)
    {
        var items = source.ToList();
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items;
    }

    public static QuizQuestion BuildOne(Card card, IReadOnlyList<string> answerPool)
    {
        return card.Kind switch
        {
            CardKind.MultipleChoice => SingleChoice(card),
            CardKind.TrueFalse => TrueFalse(card),
            CardKind.MultiSelect => MultiSelect(card),
            CardKind.FillBlanks => Typed(card, card.DisplayPrompt),
            CardKind.Order => Order(card),
            CardKind.Match => Match(card),
            _ => Recognise(card, answerPool)
        };
    }

    /// Exactly one correct option. The row is shuffled and the key is carried across.
    private static QuizQuestion SingleChoice(Card card)
    {
        var choices = card.Choices.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (choices.Count < 2)
            return Typed(card, card.Front);

        var correctText = card.CorrectChoices
            .Where(i => i >= 0 && i < choices.Count)
            .Select(i => choices[i])
            .FirstOrDefault() ?? choices[0];

        var options = Shuffle(choices).ToArray();
        var correct = Array.FindIndex(options, o => string.Equals(o, correctText, StringComparison.Ordinal));

        return new QuizQuestion
        {
            Card = card,
            Prompt = card.Front,
            Options = options,
            CorrectIndex = correct < 0 ? 0 : correct
        };
    }

    private static QuizQuestion TrueFalse(Card card)
    {
        var isTrue = card.Answer.Trim().StartsWith('t') || card.Answer.Trim().StartsWith('T');
        return new QuizQuestion
        {
            Card = card,
            Prompt = card.Front,
            Options = new[] { "True", "False" },
            CorrectIndex = isTrue ? 0 : 1
        };
    }

    private static QuizQuestion MultiSelect(Card card)
    {
        var choices = card.Choices.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

        // The stored order is the authoring order, not a quiz order. Shuffle the row and
        // carry the correct answers across by value so the key stays accurate.
        var correctText = card.CorrectChoices
            .Where(i => i >= 0 && i < choices.Count)
            .Select(i => choices[i])
            .ToHashSet(StringComparer.Ordinal);

        var options = Shuffle(choices).ToArray();
        var correct = options
            .Select((text, index) => (text, index))
            .Where(x => correctText.Contains(x.text))
            .Select(x => x.index)
            .OrderBy(i => i)
            .ToArray();

        return new QuizQuestion
        {
            Card = card,
            Prompt = card.Front,
            Options = options,
            CorrectIndexes = correct
        };
    }

    private static QuizQuestion Typed(Card card, string prompt) => new()
    {
        Card = card,
        Prompt = prompt
    };

    private static QuizQuestion Order(Card card)
    {
        var items = card.Items.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray();
        var shuffled = Shuffle(items).ToArray();

        // Never hand back the answer already in sequence.
        if (items.Length > 1 && shuffled.SequenceEqual(items))
            shuffled = items.Skip(1).Concat(items.Take(1)).ToArray();

        return new QuizQuestion
        {
            Card = card,
            Prompt = card.Front,
            Shuffled = shuffled
        };
    }

    private static QuizQuestion Match(Card card)
    {
        var rights = card.Pairs.Select(p => p.Right).Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();
        return new QuizQuestion
        {
            Card = card,
            Prompt = card.Front,
            Rights = Shuffle(rights).ToArray()
        };
    }

    private static QuizQuestion Recognise(Card card, IReadOnlyList<string> answers)
    {
        var answer = card.Answer.Trim();
        if (answer.Length == 0)
            return new QuizQuestion { Card = card, Prompt = card.Front };

        var pool = answers
            .Where(a => !string.Equals(a, answer, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var words = answer.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        // Short answers are worth typing; long answers (definitions) are recognised
        // more fairly from a list than by exact recall.
        var typeIt = words <= 4 && answer.Length <= 24 && pool.Count >= 3;

        if (typeIt || pool.Count < 1)
            return new QuizQuestion { Card = card, Prompt = card.Front };

        var distractors = Shuffle(pool).Take(3).ToList();

        var options = Shuffle(distractors.Append(answer).ToList()).ToArray();

        var correct = Array.FindIndex(options, o => string.Equals(o, answer, StringComparison.OrdinalIgnoreCase));

        return new QuizQuestion
        {
            Card = card,
            Prompt = card.Front,
            Options = options,
            CorrectIndex = correct
        };
    }
}

public static class Session
{
    /// Due cards first (oldest due date first); when nothing is due, the whole deck
    /// becomes a cram session so the button never dead-ends.
    public static List<Card> Build(IReadOnlyList<Card> cards, int limit)
    {
        var now = DateTime.Now;
        var due = cards
            .Where(c => c.IsDue(now))
            .OrderBy(c => c.Due ?? DateTime.MaxValue) // never-seen cards go last
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        var queue = due.Count > 0 ? due : cards.OrderBy(_ => Random.Shared.Next()).ToList();
        return queue.Take(Math.Max(1, limit)).ToList();
    }
}
