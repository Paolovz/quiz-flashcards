using System.Collections.ObjectModel;
using Recall.Core;
using Recall.Models;
using Recall.Services;
using Recall.Views;

namespace Recall.ViewModels;

public sealed class DayCell
{
    public DateTime Date { get; init; }
    public int Count { get; init; }
    public int Level => Count == 0 ? 0 : Count < 5 ? 1 : Count < 15 ? 2 : Count < 30 ? 3 : 4;
    public string Label => $"{Date:ddd d MMM} · {Count} review{(Count == 1 ? "" : "s")}";
}

/// <summary>One bar in the seven-day review forecast.</summary>
public sealed class ForecastBar
{
    public DateTime Date { get; init; }
    public int Count { get; init; }
    public double Height { get; init; }
    public bool IsToday { get; init; }

    public string DayLabel => IsToday ? "Today" : Date.ToString("ddd");
    public string CountLabel => Count == 0 ? "—" : Count.ToString();
    public string Tooltip => $"{Date:ddd d MMM} · {Count} card{(Count == 1 ? "" : "s")} due";
    public string Tag => IsToday ? "today" : "";
}

public sealed class LibraryViewModel : Observable
{
    private readonly INavigator _nav;
    private readonly Store _store = Store.Instance;

    private bool _isCreating;
    private string _newName = "";
    private string _newSubject = "";
    private string _createError = "";
    private string _status = "";
    private bool _nameMissing;
    private bool _subjectMissing;

    /// <summary>Vertical scroll of the page, kept so returning from Settings lands where
    /// the reader left off rather than at the top.</summary>
    public double ScrollOffset { get; set; }

    public LibraryViewModel(INavigator nav)
    {
        _nav = nav;
        CreateDeckCommand = new RelayCommand(CreateDeck);
        NewDeckCommand = new RelayCommand(StartCreate);
        CancelCommand = new RelayCommand(() =>
        {
            IsCreating = false;
            NewName = "";
            NewSubject = "";
            ClearCreateError();
        });
        OpenCommand = new RelayCommand(p => { if (p is Deck deck) _nav.ToDeck(deck); });
        DeleteCommand = new RelayCommand(p => Delete(p as Deck));
        ImportCommand = new RelayCommand(_nav.ToImport);
        ImportCsvCommand = new RelayCommand(ImportCsv);
        StudyAllCommand = new RelayCommand(StudyAll);
        Refresh();
    }

    public ObservableCollection<Deck> Decks { get; } = new();
    public ObservableCollection<DayCell> Activity { get; } = new();
    public ObservableCollection<ForecastBar> Forecast { get; } = new();

    public RelayCommand CreateDeckCommand { get; }
    public RelayCommand NewDeckCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ImportCsvCommand { get; }
    public RelayCommand StudyAllCommand { get; }

    public string Greeting => DateTime.Now.Hour switch
    {
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening"
    };

    public string Subtitle
    {
        get
        {
            var decks = _store.Data.Decks.Count;
            var cards = _store.AllCards().Count();
            return $"{decks} deck{(decks == 1 ? "" : "s")} · {cards} card{(cards == 1 ? "" : "s")}";
        }
    }

    public int DueToday => _store.DueTotal;
    public int Streak => _store.Streak();
    public int ReviewsToday => _store.ReviewsToday;

    public string MasteryText
    {
        get
        {
            var cards = _store.AllCards().ToList();
            if (cards.Count == 0) return "—";
            return $"{Math.Round(cards.Average(c => c.Mastery) * 100)}%";
        }
    }

    public string TodayHeadline => DueToday == 0 ? "All caught up" : $"{DueToday} card{(DueToday == 1 ? "" : "s")} due";
    public string TodayBody => DueToday == 0
        ? "Nothing scheduled. Start a cram session or add new cards."
        : "Reviews scheduled by the spaced repetition engine.";

    public bool HasDecks => Decks.Count > 0;
    public bool IsEmpty => Decks.Count == 0;

    public bool IsCreating
    {
        get => _isCreating;
        set => Set(ref _isCreating, value);
    }

    public string NewName
    {
        get => _newName;
        set
        {
            if (!Set(ref _newName, value)) return;
            ClearCreateError();
        }
    }

    public string NewSubject
    {
        get => _newSubject;
        set
        {
            if (!Set(ref _newSubject, value)) return;
            ClearCreateError();
        }
    }

    /// Inline validation for the create form: the empty fields go red and one summary
    /// line under them says what is missing.
    public string CreateError
    {
        get => _createError;
        private set
        {
            if (!Set(ref _createError, value)) return;
            Raise(nameof(HasCreateError));
        }
    }

    public bool HasCreateError => _createError.Length > 0;
    public string NameTag => _nameMissing ? "invalid" : "";
    public string SubjectTag => _subjectMissing ? "invalid" : "";

    /// One line that says what just happened. Set after a deck is deleted so the card
    /// leaving the grid is not the only signal.
    public string Status
    {
        get => _status;
        private set
        {
            if (!Set(ref _status, value)) return;
            Raise(nameof(HasStatus));
        }
    }

    public bool HasStatus => _status.Length > 0;

    private void ClearCreateError()
    {
        if (_createError.Length == 0) return;
        _nameMissing = false;
        _subjectMissing = false;
        CreateError = "";
        Raise(nameof(NameTag));
        Raise(nameof(SubjectTag));
    }

    private void SetCreateError(bool nameMissing, bool subjectMissing)
    {
        _nameMissing = nameMissing;
        _subjectMissing = subjectMissing;

        CreateError = (nameMissing, subjectMissing) switch
        {
            (true, true) => "Give the deck a name and a subject before creating it.",
            (true, false) => "Give the deck a name before creating it.",
            (false, true) => "Give the deck a subject before creating it.",
            _ => ""
        };

        Raise(nameof(NameTag));
        Raise(nameof(SubjectTag));
    }

    private void Refresh()
    {
        Decks.Clear();
        foreach (var deck in _store.Data.Decks.OrderBy(d => d.Name))
            Decks.Add(deck);

        BuildActivity();
        BuildForecast();

        Raise(nameof(Subtitle));
        Raise(nameof(DueToday));
        Raise(nameof(Streak));
        Raise(nameof(ReviewsToday));
        Raise(nameof(MasteryText));
        Raise(nameof(TodayHeadline));
        Raise(nameof(TodayBody));
        Raise(nameof(HasDecks));
        Raise(nameof(IsEmpty));
    }

    private void BuildActivity()
    {
        Activity.Clear();

        var today = DateTime.Today;
        var start = today.AddDays(-(int)today.DayOfWeek - 7 * 11);

        // Weekday-major order so a UniformGrid with 7 rows reads as a calendar.
        for (var weekday = 0; weekday < 7; weekday++)
        {
            for (var week = 0; week < 12; week++)
            {
                var date = start.AddDays(week * 7 + weekday);
                Activity.Add(new DayCell
                {
                    Date = date,
                    Count = date > today ? -1 : _store.Data.ReviewsByDay.GetValueOrDefault(date.ToString("yyyy-MM-dd"))
                });
            }
        }
    }

    /// <summary>
    /// Seven-day review forecast. Bars are scaled against the busiest day so a light
    /// week still reads as a shape instead of a flat line.
    /// </summary>
    private void BuildForecast()
    {
        Forecast.Clear();

        var days = _store.Forecast(7);
        var peak = Math.Max(1, days.Max(d => d.Count));
        var today = DateTime.Today;

        foreach (var (day, count) in days)
        {
            Forecast.Add(new ForecastBar
            {
                Date = day,
                Count = count,
                IsToday = day == today,
                Height = 6 + 54.0 * count / peak
            });
        }

        Raise(nameof(ForecastTotal));
    }

    /// <summary>Total cards landing in the next seven days, new cards included.</summary>
    public string ForecastTotal
    {
        get
        {
            var total = Forecast.Sum(f => f.Count);
            return total == 0 ? "Nothing scheduled" : $"{total} card{(total == 1 ? "" : "s")}";
        }
    }

    private void ImportCsv()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Delimited text (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Title = "Import cards from a CSV, TSV or Quizlet export"
        };

        if (dialog.ShowDialog() != true) return;

        var result = CsvDeck.Import(dialog.FileName);
        if (result.Error is not null)
        {
            Status = result.Error;
            return;
        }

        if (result.Cards.Count == 0)
        {
            Status = "No rows with a front and a back were found.";
            return;
        }

        var deck = new Deck
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            Subject = "Imported",
            Cards = result.Cards
        };

        _store.Data.Decks.Add(deck);
        _store.Save();
        Refresh();

        Status = result.Skipped == 0
            ? $"{result.Cards.Count} cards imported into \u201C{deck.Name}\u201D."
            : $"{result.Cards.Count} cards imported, {result.Skipped} empty rows skipped.";

        _nav.ToDeck(deck);
    }

    private void StartCreate()    {
        NewName = "";
        NewSubject = "";
        ClearCreateError();
        Status = "";
        IsCreating = true;
    }

    private void CreateDeck()
    {
        var name = NewName.Trim();
        var subject = NewSubject.Trim();

        if (name.Length == 0 || subject.Length == 0)
        {
            SetCreateError(name.Length == 0, subject.Length == 0);
            return;
        }

        var deck = new Deck
        {
            Name = name,
            Subject = subject
        };

        _store.Data.Decks.Add(deck);
        _store.Save();

        IsCreating = false;
        NewName = "";
        NewSubject = "";
        ClearCreateError();
        _nav.ToDeck(deck);
    }

    private void Delete(Deck? deck)
    {
        if (deck is null) return;

        var cards = deck.Cards.Count;
        var detail = cards == 0
            ? "The deck is empty, so nothing else goes with it."
            : $"{cards} card{(cards == 1 ? "" : "s")} and their review history go with it.";

        if (!ConfirmWindow.Ask(
                $"Delete \u201C{deck.Name}\u201D?",
                $"{detail} This cannot be undone.",
                "Delete deck"))
            return;

        _store.Data.Decks.Remove(deck);
        _store.Save();
        Refresh();
        Status = $"\u201C{deck.Name}\u201D deleted.";
    }

    private void StudyAll()
    {
        var cards = _store.AllDue().ToList();
        if (cards.Count == 0) cards = _store.AllCards().ToList();
        if (cards.Count == 0) return;

        var queue = Session.Build(cards, 20);
        _nav.ToStudy(null, queue, quiz: false);
    }
}
