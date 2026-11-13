using System.Collections.ObjectModel;
using System.ComponentModel;
using Recall.Core;
using Recall.Models;
using Recall.Services;

namespace Recall.ViewModels;

public sealed class DraftRow : Observable
{
    private bool _selected = true;
    private CardKind _targetKind;

    public DraftRow(DraftCard draft, CardKind targetKind)
    {
        Draft = draft;
        _targetKind = targetKind;
    }

    public DraftCard Draft { get; }

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// The card kind this row becomes, once the import choice is applied.
    public CardKind TargetKind
    {
        get => _targetKind;
        set
        {
            if (Set(ref _targetKind, value)) Raise(nameof(Kind));
        }
    }

    public string Front => Draft.Front;
    public string Back => Draft.Back;
    public string Source => Draft.Source ?? "";

    public string Kind => TargetKind switch
    {
        CardKind.MultipleChoice => "Multiple choice",
        CardKind.MultiSelect => "Multi-select",
        CardKind.FillBlanks => "Fill the blanks",
        _ => "Q&A"
    };
}

/// One entry in the import card-type picker. A null Kind means "keep what the
/// extractor found" — a cloze stays a fill-the-blanks card, a definition stays Q&A.
public sealed class ImportKindOption
{
    public ImportKindOption(CardKind? kind, string label, string help)
    {
        Kind = kind;
        Label = label;
        Help = help;
    }

    public CardKind? Kind { get; }
    public string Label { get; }
    public string Help { get; }
}

public sealed class ImportViewModel : Observable
{
    private readonly INavigator _nav;
    private readonly Store _store = Store.Instance;

    private string _fileName = "";
    private string _status = "";
    private string _maxCardsText = "25";
    private ImportKindOption _selectedKind;
    private bool _isBusy;
    private bool _hasResult;
    private int _pages;

    public ImportViewModel(INavigator nav)
    {
        _nav = nav;
        _selectedKind = KindOptions[0];

        ChooseFileCommand = new RelayCommand(async _ => await LoadAsync());
        AddCommand = new RelayCommand(AddToDeck, () => HasResult && SelectedCount > 0);
        CancelCommand = new RelayCommand(_nav.ToLibrary);
        SelectAllCommand = new RelayCommand(_ => SetAll(true));
        SelectNoneCommand = new RelayCommand(_ => SetAll(false));
    }

    public ObservableCollection<DraftRow> Drafts { get; } = new();

    public IReadOnlyList<ImportKindOption> KindOptions { get; } = new[]
    {
        new ImportKindOption(null, "Auto",
            "Keep what the extractor found: cloze sentences become fill-the-blanks, definitions become Q&A."),
        new ImportKindOption(CardKind.Qa, "Q&A",
            "Every card asks the question and takes a typed answer."),
        new ImportKindOption(CardKind.FillBlanks, "Fill the blanks",
            "Every card blanks the key term out of the sentence and takes a typed answer."),
        new ImportKindOption(CardKind.MultipleChoice, "Multiple choice",
            "Every card becomes a row of four answers with one correct option — click one to answer."),
        new ImportKindOption(CardKind.MultiSelect, "Multi-select",
            "Every card becomes a row of answers where one or more are correct — tick every one.")
    };

    public RelayCommand ChooseFileCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }

    public ImportKindOption SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedKind)) return;
            _selectedKind = value;
            Raise(nameof(SelectedKind));
            Raise(nameof(KindHelp));
            ApplyKind();
        }
    }

    public string KindHelp => SelectedKind.Help;

    public string FileName
    {
        get => _fileName;
        private set => Set(ref _fileName, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string MaxCardsText
    {
        get => _maxCardsText;
        set => Set(ref _maxCardsText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            ChooseFileCommand.RaiseCanExecuteChanged();
            AddCommand.RaiseCanExecuteChanged();
            Raise(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsBusy;

    public bool HasResult
    {
        get => _hasResult;
        private set
        {
            if (!Set(ref _hasResult, value)) return;
            AddCommand.RaiseCanExecuteChanged();
            Raise(nameof(HasNoResult));
        }
    }

    public bool HasNoResult => !HasResult;

    public int SelectedCount => Drafts.Count(d => d.Selected);
    public string SelectionLabel => $"{SelectedCount} of {Drafts.Count} selected";
    public string PageLabel => _pages == 1 ? "1 page" : $"{_pages} pages";

    private int ParseMax()
    {
        if (!int.TryParse(MaxCardsText.Trim(), out var value)) return 25;
        return Math.Clamp(value, 5, 200);
    }

    private async Task LoadAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*",
            CheckFileExists = true,
            Title = "Choose a PDF to turn into cards"
        };

        if (dialog.ShowDialog() != true) return;

        var path = dialog.FileName;
        var max = ParseMax();
        MaxCardsText = max.ToString();

        IsBusy = true;
        Status = "Reading the document…";

        var progress = new Progress<string>(message => Status = message);
        var extraction = await Task.Run(() => PdfImport.FromFile(path, max, progress));

        IsBusy = false;

        if (extraction.Error is not null)
        {
            HasResult = false;
            Status = extraction.Error;
            return;
        }

        Drafts.Clear();
        foreach (var draft in extraction.Cards)
        {
            var row = new DraftRow(draft, Resolve(draft));
            row.PropertyChanged += OnRowChanged;
            Drafts.Add(row);
        }

        FileName = extraction.FileName;
        _pages = extraction.Pages;
        HasResult = Drafts.Count > 0;
        Status = Drafts.Count == 0
            ? "No card-shaped sentences found. Try a text-heavy document."
            : $"{Drafts.Count} cards from {PageLabel}";

        Raise(nameof(SelectedCount));
        Raise(nameof(SelectionLabel));
        Raise(nameof(PageLabel));
        AddCommand.RaiseCanExecuteChanged();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DraftRow.Selected)) return;
        Raise(nameof(SelectedCount));
        Raise(nameof(SelectionLabel));
        AddCommand.RaiseCanExecuteChanged();
    }

    private void SetAll(bool selected)
    {
        foreach (var row in Drafts) row.Selected = selected;
    }

    private CardKind Resolve(DraftCard draft)
        => SelectedKind.Kind ?? (draft.Kind == "cloze" ? CardKind.FillBlanks : CardKind.Qa);

    private void ApplyKind()
    {
        foreach (var row in Drafts)
            row.TargetKind = Resolve(row.Draft);
    }

    private void AddToDeck()
    {
        var selected = Drafts.Where(d => d.Selected).ToList();
        if (selected.Count == 0)
        {
            Status = "Select at least one card.";
            return;
        }

        var deck = new Deck
        {
            Name = string.IsNullOrWhiteSpace(FileName) ? "Imported deck" : FileName,
            Subject = "Imported"
        };

        // Decoys for multi-select come from the rest of the batch, so the choices are
        // real answers from this document rather than invented text.
        var answers = selected
            .Select(r => r.Draft.Back.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var row in selected)
        {
            var draft = row.Draft;
            var kind = row.TargetKind;
            var card = new Card
            {
                Kind = kind,
                Front = draft.Front,
                Back = draft.Back,
                Source = draft.Source
            };

            if (kind is CardKind.MultipleChoice or CardKind.MultiSelect)
            {
                var answer = draft.Back.Trim();
                var choices = new List<string> { answer };

                // Prefer decoys close in length to the answer: a four-word option next to a
                // two-line definition gives the answer away by shape alone.
                foreach (var decoy in answers
                             .Where(a => !string.Equals(a, answer, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(a => Math.Abs(a.Length - answer.Length))
                             .Take(3))
                {
                    choices.Add(decoy);
                }

                if (choices.Count < 2)
                {
                    card.Kind = CardKind.Qa;
                }
                else
                {
                    var ordered = QuizFactory.Shuffle(choices).ToList();
                    card.Choices = ordered;
                    card.CorrectChoices = new List<int> { ordered.IndexOf(answer) };
                }
            }

            deck.Cards.Add(card);
        }

        _store.Data.Decks.Add(deck);
        _store.Save();
        _nav.ToDeck(deck);
    }
}
