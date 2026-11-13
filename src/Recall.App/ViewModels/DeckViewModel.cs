using System.Collections.ObjectModel;
using System.IO;
using Recall.Core;
using Recall.Models;
using Recall.Services;
using Recall.Views;

namespace Recall.ViewModels;

public sealed class KindOption
{
    public KindOption(CardKind kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    public CardKind Kind { get; }
    public string Label { get; }
}

public sealed class DeckViewModel : Observable
{
    private readonly INavigator _nav;
    private readonly Store _store = Store.Instance;
    private readonly Deck _deck;

    private Card? _selected;
    private CardKind _kind = CardKind.Qa;
    private string _front = "";
    private string _back = "";
    private string _hint = "";
    private string _choicesText = "";
    private string _itemsText = "";
    private string _pairsText = "";
    private string _imagePath = "";
    private bool _answerIsTrue = true;
    private string _search = "";
    private string _status = "";
    private string _tags = "";
    private bool _starred;
    private bool _suspended;

    public DeckViewModel(INavigator nav, Deck deck)
    {
        _nav = nav;
        _deck = deck;

        BackCommand = new RelayCommand(_nav.ToLibrary);
        StudyCommand = new RelayCommand(() => Start(quiz: false, all: false));
        QuizCommand = new RelayCommand(() => Start(quiz: true, all: false));
        CramCommand = new RelayCommand(() => Start(quiz: false, all: true));
        StudyStarredCommand = new RelayCommand(StudyStarred, () => _deck.StarredCount > 0);
        AddCommand = new RelayCommand(NewCard);
        SaveCommand = new RelayCommand(Save);
        DeleteCardCommand = new RelayCommand(DeleteCard);
        ExportCommand = new RelayCommand(Export);
        ImportCsvCommand = new RelayCommand(ImportCsv);
        ImportPdfCommand = new RelayCommand(_nav.ToImport);
        BrowseImageCommand = new RelayCommand(BrowseImage);
        ToggleStarCommand = new RelayCommand(ToggleStar);
        ToggleSuspendCommand = new RelayCommand(ToggleSuspend);

        Refresh();
    }

    public Deck Deck => _deck;
    public ObservableCollection<Card> Cards { get; } = new();

    public IReadOnlyList<KindOption> KindOptions { get; } = new[]
    {
        new KindOption(CardKind.Qa, "Q&A"),
        new KindOption(CardKind.MultipleChoice, "Multiple choice"),
        new KindOption(CardKind.MultiSelect, "Multi-select"),
        new KindOption(CardKind.FillBlanks, "Fill the blanks"),
        new KindOption(CardKind.Order, "Order items"),
        new KindOption(CardKind.Match, "Match pairs"),
        new KindOption(CardKind.TrueFalse, "True or false"),
        new KindOption(CardKind.Image, "Image question")
    };

    public RelayCommand BackCommand { get; }
    public RelayCommand StudyCommand { get; }
    public RelayCommand QuizCommand { get; }
    public RelayCommand CramCommand { get; }
    public RelayCommand StudyStarredCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand DeleteCardCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ImportCsvCommand { get; }
    public RelayCommand ImportPdfCommand { get; }
    public RelayCommand BrowseImageCommand { get; }
    public RelayCommand ToggleStarCommand { get; }
    public RelayCommand ToggleSuspendCommand { get; }

    public string Name
    {
        get => _deck.Name;
        set
        {
            if (_deck.Name == value) return;
            _deck.Name = value;
            _store.Save();
            Raise();
        }
    }

    public string Subject
    {
        get => _deck.Subject;
        set
        {
            if (_deck.Subject == value) return;
            _deck.Subject = value;
            _store.Save();
            Raise();
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value)) return;
            RefreshCards();
        }
    }

    public CardKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            Raise(nameof(SelectedKind));
            RaiseKindFlags();
            Raise(nameof(FrontLabel));
            Raise(nameof(BackLabel));
            Raise(nameof(KindHelp));
        }
    }

    public KindOption? SelectedKind
    {
        get => KindOptions.FirstOrDefault(k => k.Kind == Kind);
        set
        {
            if (value is null || value.Kind == Kind) return;
            Kind = value.Kind;
        }
    }

    public bool AnswerIsFalse
    {
        get => !AnswerIsTrue;
        set => AnswerIsTrue = !value;
    }

    public Card? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            if (value is not null) Load(value);
            Raise(nameof(EditorTitle));
            Raise(nameof(IsEditing));
            Raise(nameof(DeleteLabel));
        }
    }

    public string Front
    {
        get => _front;
        set => Set(ref _front, value);
    }

    public string Back
    {
        get => _back;
        set => Set(ref _back, value);
    }

    public string Hint
    {
        get => _hint;
        set => Set(ref _hint, value);
    }

    public string ChoicesText
    {
        get => _choicesText;
        set => Set(ref _choicesText, value);
    }

    public string ItemsText
    {
        get => _itemsText;
        set => Set(ref _itemsText, value);
    }

    public string PairsText
    {
        get => _pairsText;
        set => Set(ref _pairsText, value);
    }

    public string ImagePath
    {
        get => _imagePath;
        set => Set(ref _imagePath, value);
    }

    public string Tags
    {
        get => _tags;
        set => Set(ref _tags, value);
    }

    public bool IsStarred
    {
        get => _starred;
        set
        {
            if (!Set(ref _starred, value)) return;
            Raise(nameof(StarLabel));
            Raise(nameof(StarTag));
        }
    }

    public bool IsSuspended
    {
        get => _suspended;
        set
        {
            if (!Set(ref _suspended, value)) return;
            Raise(nameof(SuspendLabel));
            Raise(nameof(SuspendTag));
        }
    }

    public string StarLabel => IsStarred ? "Starred" : "Star";
    public string StarTag => IsStarred ? "on" : "";
    public string SuspendLabel => IsSuspended ? "Suspended" : "Suspend";
    public string SuspendTag => IsSuspended ? "on" : "";
    public bool HasStarred => _deck.StarredCount > 0;
    public string StarredLabel => _deck.StarredCount == 0 ? "Study starred" : $"Study starred ({_deck.StarredCount})";

    public bool AnswerIsTrue
    {
        get => _answerIsTrue;
        set
        {
            if (!Set(ref _answerIsTrue, value)) return;
            Raise(nameof(AnswerIsFalse));
        }
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public bool IsEditing => Selected is not null;
    public string EditorTitle => Selected is null ? "New card" : "Edit card";
    public string DeleteLabel => "Delete card";

    public bool IsQa => Kind is CardKind.Qa;
    public bool IsMultipleChoice => Kind is CardKind.MultipleChoice;
    public bool IsMultiSelect => Kind is CardKind.MultiSelect;
    public bool IsFillBlanks => Kind is CardKind.FillBlanks;
    public bool IsOrder => Kind is CardKind.Order;
    public bool IsMatch => Kind is CardKind.Match;
    public bool IsTrueFalse => Kind is CardKind.TrueFalse;
    public bool IsImage => Kind is CardKind.Image;

    /// Both choice kinds edit the same options box; only the rule differs.
    public bool ShowsChoices => Kind is CardKind.MultipleChoice or CardKind.MultiSelect;
    public bool HasBackField => Kind is not (CardKind.MultipleChoice or CardKind.MultiSelect or CardKind.Order
        or CardKind.Match or CardKind.TrueFalse);

    public string FrontLabel => Kind switch
    {
        CardKind.MultipleChoice => "QUESTION · what are you choosing between?",
        CardKind.MultiSelect => "QUESTION · what are you selecting from?",
        CardKind.FillBlanks => "SENTENCE · wrap words in {{ }} to blank them",
        CardKind.Order => "PROMPT · what should be put in order?",
        CardKind.Match => "PROMPT · what is being matched?",
        CardKind.TrueFalse => "STATEMENT",
        CardKind.Image => "PROMPT · what should the reader identify?",
        _ => "FRONT · question or prompt"
    };

    public string BackLabel => Kind switch
    {
        CardKind.FillBlanks => "ANSWERS · separate alternatives with |",
        CardKind.Image => "ANSWER",
        _ => "BACK · answer"
    };

    public string KindHelp => Kind switch
    {
        CardKind.MultipleChoice => "One option per line. Mark the single correct option with *.",
        CardKind.MultiSelect => "One option per line. Mark every correct option with * — the reader picks one or more.",
        CardKind.Order => "One item per line, top to bottom is the correct order. The quiz shuffles them.",
        CardKind.Match => "One pair per line as left = right.",
        CardKind.TrueFalse => "Pick the answer; the quiz offers both options.",
        CardKind.FillBlanks => "Each {{word}} becomes a blank. Answers go in the answer field.",
        CardKind.Image => "Point at an image on disk. The answer is typed by the reader.",
        _ => "Answers may list alternatives with |. Put the key term first, then a comma and any detail — typing the key term alone counts as correct."
    };

    public string CountLabel => _deck.Cards.Count == 1 ? "1 card" : $"{_deck.Cards.Count} cards";
    public string DueLabel => $"{_deck.DueCount} due";
    public string MasteryText => $"{Math.Round(_deck.Mastery * 100)}%";
    public double MasteryValue => _deck.Mastery * 100;

    public bool HasCards => _deck.Cards.Count > 0;
    public bool IsEmpty => _deck.Cards.Count == 0;
    public bool CanQuiz => _deck.Cards.Count >= 2;
    public bool HasNoMatches => Cards.Count == 0 && _deck.Cards.Count > 0;

    public void Refresh()
    {
        Raise(nameof(Name));
        Raise(nameof(Subject));
        Raise(nameof(CountLabel));
        Raise(nameof(DueLabel));
        Raise(nameof(MasteryText));
        Raise(nameof(MasteryValue));
        Raise(nameof(HasCards));
        Raise(nameof(IsEmpty));
        Raise(nameof(CanQuiz));
        Raise(nameof(HasStarred));
        Raise(nameof(StarredLabel));
        StudyStarredCommand.RaiseCanExecuteChanged();
        RefreshCards();
    }

    private void RaiseKindFlags()
    {
        Raise(nameof(IsQa));
        Raise(nameof(IsMultipleChoice));
        Raise(nameof(IsMultiSelect));
        Raise(nameof(IsFillBlanks));
        Raise(nameof(IsOrder));
        Raise(nameof(IsMatch));
        Raise(nameof(IsTrueFalse));
        Raise(nameof(IsImage));
        Raise(nameof(ShowsChoices));
        Raise(nameof(HasBackField));
    }

    private void RefreshCards()
    {
        Cards.Clear();
        var query = Search.Trim();
        var source = string.IsNullOrEmpty(query)
            ? _deck.Cards
            : _deck.Cards.Where(c =>
                c.Front.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Back.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (c.Tags ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var card in source.OrderBy(c => c.Front))
            Cards.Add(card);

        Raise(nameof(HasNoMatches));
    }

    private void Load(Card card)
    {
        Kind = card.Kind;
        Front = card.Front;
        Back = card.Back;
        Hint = card.Hint ?? "";
        ChoicesText = CardFormat.FormatChoices(card.Choices, card.CorrectChoices);
        ItemsText = CardFormat.FormatItems(card.Items);
        PairsText = CardFormat.FormatPairs(card.Pairs);
        ImagePath = card.ImagePath ?? "";
        AnswerIsTrue = !card.Answer.Trim().Equals("False", StringComparison.OrdinalIgnoreCase);
        Tags = card.Tags ?? "";
        IsStarred = card.Starred;
        IsSuspended = card.Suspended;
    }

    private void NewCard()
    {
        Selected = null;
        Kind = CardKind.Qa;
        Front = "";
        Back = "";
        Hint = "";
        ChoicesText = "";
        ItemsText = "";
        PairsText = "";
        ImagePath = "";
        AnswerIsTrue = true;
        Tags = "";
        IsStarred = false;
        IsSuspended = false;
        Status = "";
        Raise(nameof(EditorTitle));
        Raise(nameof(IsEditing));
    }

    private void Save()
    {
        var front = Front.Trim();
        if (front.Length == 0)
        {
            Status = "The question is required.";
            return;
        }

        var card = Selected ?? new Card { Kind = Kind };
        card.Kind = Kind;
        card.Front = front;
        card.Hint = Hint.Trim().Length == 0 ? null : Hint.Trim();

        switch (Kind)
        {
            case CardKind.MultipleChoice:
            {
                var (choices, correct) = CardFormat.ParseChoices(ChoicesText);
                if (choices.Count < 2)
                {
                    Status = "Add at least two options.";
                    return;
                }
                if (correct.Count == 0)
                {
                    Status = "Mark the correct option with *.";
                    return;
                }
                if (correct.Count > 1)
                {
                    Status = "Multiple choice takes exactly one * — use Multi-select for several.";
                    return;
                }
                card.Choices = choices;
                card.CorrectChoices = correct;
                card.Back = choices[correct[0]];
                break;
            }

            case CardKind.MultiSelect:
            {
                var (choices, correct) = CardFormat.ParseChoices(ChoicesText);
                if (choices.Count < 2)
                {
                    Status = "Add at least two options.";
                    return;
                }
                if (correct.Count == 0)
                {
                    Status = "Mark at least one option correct with *.";
                    return;
                }
                card.Choices = choices;
                card.CorrectChoices = correct;
                card.Back = string.Join(" | ", correct.Select(i => choices[i]));
                break;
            }

            case CardKind.Order:
            {
                var items = CardFormat.ParseItems(ItemsText);
                if (items.Count < 2)
                {
                    Status = "Add at least two items, one per line.";
                    return;
                }
                card.Items = items;
                card.Back = string.Join(" → ", items);
                break;
            }

            case CardKind.Match:
            {
                var pairs = CardFormat.ParsePairs(PairsText);
                if (pairs.Count < 2)
                {
                    Status = "Add at least two pairs, written left = right.";
                    return;
                }
                card.Pairs = pairs;
                card.Back = string.Join(" | ", pairs.Select(p => p.Right));
                break;
            }

            case CardKind.TrueFalse:
                card.Back = AnswerIsTrue ? "True" : "False";
                break;

            case CardKind.Image:
            {
                var back = Back.Trim();
                if (back.Length == 0)
                {
                    Status = "The answer is required.";
                    return;
                }
                if (ImagePath.Trim().Length > 0 && !File.Exists(ImagePath.Trim()))
                {
                    Status = "That image file is not there.";
                    return;
                }
                card.Back = back;
                card.ImagePath = ImagePath.Trim().Length == 0 ? null : ImagePath.Trim();
                break;
            }

            default:
            {
                var back = Back.Trim();
                if (back.Length == 0)
                {
                    Status = "The answer is required.";
                    return;
                }
                card.Back = back;
                break;
            }
        }

        if (Selected is null) _deck.Cards.Add(card);

        card.Tags = Tags.Trim().Length == 0 ? null : Tags.Trim();
        card.Starred = IsStarred;
        card.Suspended = IsSuspended;

        _store.Save();
        Refresh();
        Selected = card;
        Status = "Saved.";
    }

    private void DeleteCard()
    {
        if (Selected is null) return;

        var front = Selected.Front.Trim();
        var label = front.Length > 0 ? front : "this card";
        if (label.Length > 70) label = label[..67] + "…";

        if (!ConfirmWindow.Ask(
                "Delete this card?",
                $"\u201C{label}\u201D and its review history are removed for good. This cannot be undone.",
                "Delete card"))
            return;

        _deck.Cards.Remove(Selected);
        _store.Save();
        NewCard();
        Refresh();
        Status = "Card deleted.";
    }

    private void BrowseImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files (*.*)|*.*",
            CheckFileExists = true,
            Title = "Choose the image for this card"
        };

        if (dialog.ShowDialog() == true) ImagePath = dialog.FileName;
    }

    private void Start(bool quiz, bool all)
    {
        if (_deck.Cards.Count == 0) return;

        var pool = all ? _deck.Cards : _deck.Cards.Where(c => c.IsDue(DateTime.Now)).ToList();
        if (pool.Count == 0) pool = _deck.Cards;

        var queue = Session.Build(pool, 20);
        _nav.ToStudy(_deck, queue, quiz && _deck.Cards.Count >= 2);
    }

    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{CsvDeck.SafeFileName(_deck.Name)}.csv",
            Filter = "CSV for Anki, Quizlet or Excel (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            Title = "Export this deck as CSV"
        };

        if (dialog.ShowDialog() != true) return;

        CsvDeck.Export(_deck.Cards, dialog.FileName);
        Status = $"Exported {_deck.Cards.Count} cards to {System.IO.Path.GetFileName(dialog.FileName)}.";
    }

    /// <summary>
    /// Adds the rows of a CSV into the deck that is open, so a spreadsheet can top up an
    /// existing deck instead of always starting a new one.
    /// </summary>
    private void ImportCsv()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Delimited text (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Title = "Add cards from a CSV or TSV"
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

        foreach (var card in result.Cards) _deck.Cards.Add(card);

        _store.Save();
        Refresh();

        Status = result.Skipped == 0
            ? $"Added {result.Cards.Count} cards from {System.IO.Path.GetFileName(dialog.FileName)}."
            : $"Added {result.Cards.Count} cards, skipped {result.Skipped} empty rows.";
    }

    private void ToggleStar()
    {
        if (Selected is null) return;
        IsStarred = !IsStarred;
        Selected.Starred = IsStarred;
        _store.Save();
        Raise(nameof(HasStarred));
        Raise(nameof(StarredLabel));
        StudyStarredCommand.RaiseCanExecuteChanged();
    }

    private void ToggleSuspend()
    {
        if (Selected is null) return;
        IsSuspended = !IsSuspended;
        Selected.Suspended = IsSuspended;
        _store.Save();
        Refresh();
        Status = IsSuspended
            ? "Suspended. It stays out of sessions until you unsuspend it."
            : "Back in rotation.";
    }

    private void StudyStarred()
    {
        var starred = _deck.Cards.Where(c => c.Starred && !c.Suspended).ToList();
        if (starred.Count == 0) return;

        _nav.ToStudy(_deck, Session.Build(starred, 50), quiz: false);
    }
}
