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

    /// <summary>
    /// Decoys for a recognition question, taken from the deck's own answers. An option
    /// list is only fair when every wrong option is a genuinely different answer, so a
    /// boolean, a stub, a second spelling of the answer, or an answer that contains the
    /// answer is dropped. The rest are sorted by length so the correct option is not
    /// given away by being the only paragraph in a list of four words.
    /// </summary>
    public static List<string> PickDistractors(string answer, IEnumerable<string> pool, int count = 3)
    {
        return Shuffle(pool
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList())
            .Where(a => !IsBooleanish(a))
            .Where(a => a.Length >= 3)
            .Where(a => !TooClose(answer, a))
            .OrderBy(a => Math.Abs(a.Length - answer.Length))
            .Take(count)
            .ToList();
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

        var pool = PickDistractors(answer, answers);

        var words = answer.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        // Short answers are worth typing; long answers (definitions) are recognised
        // more fairly from a list than by exact recall.
        var typeIt = words <= 4 && answer.Length <= 24 && pool.Count >= 3;

        if (typeIt || pool.Count < 1)
            return new QuizQuestion { Card = card, Prompt = card.Front };

        var options = Shuffle(pool.Append(answer).ToList()).ToArray();

        var correct = Array.FindIndex(options, o => string.Equals(o, answer, StringComparison.OrdinalIgnoreCase));

        return new QuizQuestion
        {
            Card = card,
            Prompt = card.Front,
            Options = options,
            CorrectIndex = correct
        };
    }

    /// <summary>"True" is a valid option on a true/false card and nonsense on every other.</summary>
    private static bool IsBooleanish(string value)
    {
        var v = value.Trim();
        return v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("false", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A distractor has to be a different answer. "Mitochondrion", "mitochondria" and
    /// "Mitochondria, the site of aerobic respiration" are one answer written three ways.
    /// </summary>
    private static bool TooClose(string answer, string candidate)
    {
        var a = TextMatch.Normalize(answer);
        var b = TextMatch.Normalize(candidate);
        if (a.Length == 0 || b.Length == 0) return true;
        if (a == b) return true;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)) return true;

        // Singular and plural, or a one-letter slip: the same word twice on one list.
        if (SharedPrefix(a, b) >= 6 && EditDistance(a, b) <= 2) return true;

        var keyA = TextMatch.KeyTerm(answer);
        var keyB = TextMatch.KeyTerm(candidate);
        if (keyA.Length < 4 || keyB.Length < 4) return false;

        return keyA == keyB
               || keyA.Contains(keyB, StringComparison.Ordinal)
               || keyB.Contains(keyA, StringComparison.Ordinal);
    }

    private static int SharedPrefix(string a, string b)
    {
        var limit = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < limit && a[i] == b[i]) i++;
        return i;
    }

    /// <summary>Levenshtein distance, abandoned as soon as it passes the ceiling.</summary>
    private static int EditDistance(string a, string b, int ceiling = 2)
    {
        if (Math.Abs(a.Length - b.Length) > ceiling) return ceiling + 1;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                if (current[j] < rowMin) rowMin = current[j];
            }

            if (rowMin > ceiling) return ceiling + 1;
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}

public static class Session
{
    /// Due cards first (oldest due date first); when nothing is due, the whole deck
    /// becomes a cram session so the button never dead-ends. Suspended cards stay out.
    public static List<Card> Build(IReadOnlyList<Card> cards, int limit)
    {
        var now = DateTime.Now;
        var live = cards.Where(c => !c.Suspended).ToList();
        if (live.Count == 0) return new List<Card>();

        var due = live
            .Where(c => c.IsDue(now))
            .OrderBy(c => c.Due ?? DateTime.MaxValue) // never-seen cards go last
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        var queue = due.Count > 0 ? due : live.OrderBy(_ => Random.Shared.Next()).ToList();
        return queue.Take(Math.Max(1, limit)).ToList();
    }
}
