using System.Text.Json.Serialization;

namespace Recall.Models;

public enum CardKind
{
    Qa,
    MultiSelect,
    FillBlanks,
    Order,
    Match,
    TrueFalse,
    Image,

    /// Single-answer multiple choice: a row of options, exactly one correct.
    /// Appended last so stored enum values keep their meaning.
    MultipleChoice
}

public sealed class MatchPair
{
    public string Left { get; set; } = "";
    public string Right { get; set; } = "";
}

public sealed class Card : System.ComponentModel.INotifyPropertyChanged
{
    private static readonly System.Text.RegularExpressions.Regex BlankGap =
        new(@"\{\{(.+?)\}\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public Guid Id { get; set; } = Guid.NewGuid();
    public CardKind Kind { get; set; } = CardKind.Qa;

    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public string? Hint { get; set; }
    public string? Source { get; set; }

    /// Space-separated tags, Anki style. Search matches them.
    public string? Tags { get; set; }

    /// Starred cards can be studied on their own, the way Quizlet stars a term.
    public bool Starred
    {
        get => _starred;
        set
        {
            if (_starred == value) return;
            _starred = value;
            Raise(nameof(Starred));
        }
    }

    /// A leech that kept lapsing is pulled out of the rotation until it is unsuspended.
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            Raise(nameof(Suspended));
            Raise(nameof(StatusLabel));
        }
    }

    private bool _starred;
    private bool _suspended;

    // MultiSelect: choices plus the indexes of the correct ones.
    public List<string> Choices { get; set; } = new();
    public List<int> CorrectChoices { get; set; } = new();

    // Order: items in the correct sequence. The quiz shuffles them.
    public List<string> Items { get; set; } = new();

    // Match: left/right pairs.
    public List<MatchPair> Pairs { get; set; } = new();

    // Image question: file on disk, shown above the prompt.
    public string? ImagePath { get; set; }

    // Spaced repetition state
    public double Ease { get; set; } = 2.5;
    public int IntervalDays { get; set; }
    public int Reps { get; set; }
    public int Lapses { get; set; }
    public DateTime? Due { get; set; }

    // Results
    public int Correct { get; set; }
    public int Attempts { get; set; }

    public DateTime Created { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string[] Accepted => Back
        .Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [JsonIgnore]
    public string Answer => Accepted.Length > 0 ? Accepted[0] : Back;

    [JsonIgnore]
    public bool IsNew => Attempts == 0;

    [JsonIgnore]
    public string[] TagList => (Tags ?? "")
        .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [JsonIgnore]
    public bool IsLeech => Lapses >= SrsLeechThreshold;

    /// A card that lapsed this many times is pulled out of rotation, as Anki does.
    public const int SrsLeechThreshold = 8;

    [JsonIgnore]
    public string StatusLabel => Suspended ? "Suspended" : IsNew ? "New" : DueLabel;

    [JsonIgnore]
    public double Mastery => Attempts == 0
        ? 0
        : Math.Clamp(Correct * 0.6 / Attempts + Math.Min(Reps, 5) * 0.08, 0, 1);

    /// The prompt with any {{blanks}} replaced by a visible gap.
    [JsonIgnore]
    public string DisplayPrompt => Kind == CardKind.FillBlanks
        ? BlankGap.Replace(Front, "______")
        : Front;

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        CardKind.MultipleChoice => "Multiple choice",
        CardKind.MultiSelect => "Multi-select",
        CardKind.FillBlanks => "Fill the blanks",
        CardKind.Order => "Order items",
        CardKind.Match => "Match pairs",
        CardKind.TrueFalse => "True or false",
        CardKind.Image => "Image question",
        _ => "Q&A"
    };

    public bool IsDue(DateTime now) => Due is null || Due <= now;

    [JsonIgnore]
    public string DueLabel
    {
        get
        {
            if (IsNew) return "New";
            if (Due is null) return "Due";
            var today = DateTime.Today;
            if (Due <= DateTime.Now && Due.Value.Date <= today) return "Due";
            if (Due.Value.Date == today) return "Later today";
            if (Due.Value.Date == today.AddDays(1)) return "Tomorrow";
            var days = (Due.Value.Date - today).Days;
            return days < 30 ? $"{days}d" : $"{days / 30}mo";
        }
    }

}

public sealed class Deck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled deck";
    public string Subject { get; set; } = "General";
    public DateTime Created { get; set; } = DateTime.Now;
    public List<Card> Cards { get; set; } = new();

    [JsonIgnore] public int DueCount => Cards.Count(c => !c.Suspended && c.IsDue(DateTime.Now));
    [JsonIgnore] public int NewCount => Cards.Count(c => !c.Suspended && c.IsNew);
    [JsonIgnore] public int StarredCount => Cards.Count(c => c.Starred);
    [JsonIgnore] public int SuspendedCount => Cards.Count(c => c.Suspended);
    [JsonIgnore] public double Mastery => Cards.Count == 0 ? 0 : Cards.Average(c => c.Mastery);
    [JsonIgnore] public int Reviews => Cards.Sum(c => c.Attempts);
    [JsonIgnore] public string CountLabel => Cards.Count == 1 ? "1 card" : $"{Cards.Count} cards";
}

public sealed class AppData
{
    public List<Deck> Decks { get; set; } = new();
    public Dictionary<string, int> ReviewsByDay { get; set; } = new();
    public DateTime Installed { get; set; } = DateTime.Now;

    /// Palette id chosen in Settings: clay, deepocean, sandstone, forest.
    public string Theme { get; set; } = "clay";
}

public sealed class SessionResult
{
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "Study";
    public int Total { get; init; }
    public int Correct { get; init; }
    public int Seconds { get; init; }
    public List<string> Missed { get; init; } = new();
    public List<Card> Cards { get; init; } = new();

    [JsonIgnore] public double Accuracy => Total == 0 ? 0 : Correct * 100.0 / Total;
    [JsonIgnore] public string AccuracyLabel => $"{Math.Round(Accuracy)}%";
    [JsonIgnore] public string DurationLabel => Seconds < 60 ? $"{Seconds}s" : $"{Seconds / 60}m {Seconds % 60}s";
}
