using System.Collections.ObjectModel;
using Recall.Core;
using Recall.Models;
using Recall.Services;

namespace Recall.ViewModels;

public sealed class QuizOption : Observable
{
    private string _state = "idle";
    private bool _selected;

    public QuizOption(int index, string text)
    {
        Key = ((char)('A' + index)).ToString();
        Text = text;
    }

    public string Key { get; }
    public string Text { get; }

    public string State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }
}

public sealed class OrderItem : Observable
{
    private string _state = "idle";

    public OrderItem(string text) => Text = text;

    public string Text { get; }

    public string State
    {
        get => _state;
        set => Set(ref _state, value);
    }
}

public sealed class MatchRow : Observable
{
    private string _selectedRight = "";
    private string _state = "idle";

    public MatchRow(string left, IReadOnlyList<string> rights)
    {
        Left = left;
        Rights = rights;
    }

    public string Left { get; }
    public IReadOnlyList<string> Rights { get; }

    public string SelectedRight
    {
        get => _selectedRight;
        set => Set(ref _selectedRight, value);
    }

    public string State
    {
        get => _state;
        set => Set(ref _state, value);
    }
}

public sealed class QuizViewModel : Observable
{
    private readonly INavigator _nav;
    private readonly Deck? _deck;
    private readonly List<QuizQuestion> _questions;
    private readonly string _title;
    private readonly DateTime _started = DateTime.Now;
    private readonly List<(Card Card, bool Correct)> _results = new();

    private int _index;
    private int _score;
    private bool _answered;
    private bool _wasCorrect;
    private string _typedInput = "";
    private string _notice = "";

    /// Order questions start shuffled away from the answer, so an untouched row has not
    /// been answered yet. Set by the first move.
    private bool _orderTouched;

    public QuizViewModel(INavigator nav, Deck? deck, IReadOnlyList<Card> cards, string title)
    {
        _nav = nav;
        _deck = deck;
        _title = title;

        if (deck is null)
        {
            _questions = QuizFactory.Shuffle(
                cards.Select(c => QuizFactory.BuildOne(c, Array.Empty<string>())).ToList());
        }
        else
        {
            _questions = QuizFactory.Build(deck, cards);
        }

        PickCommand = new RelayCommand(p => { if (p is QuizOption option) Pick(option); }, _ => !Answered);
        SubmitCommand = new RelayCommand(SubmitTyped, () => !Answered && TypedInput.Trim().Length > 0);
        SubmitMultiCommand = new RelayCommand(SubmitMulti, () => !Answered && IsMultiSelect);
        SubmitOrderCommand = new RelayCommand(SubmitOrder, () => !Answered && IsOrder);
        SubmitMatchCommand = new RelayCommand(SubmitMatch, () => !Answered && IsMatch);
        MoveUpCommand = new RelayCommand(p => Move(p as OrderItem, -1), _ => !Answered);
        MoveDownCommand = new RelayCommand(p => Move(p as OrderItem, 1), _ => !Answered);
        NextCommand = new RelayCommand(Next);
        ExitCommand = new RelayCommand(() => { if (_deck is not null) _nav.ToDeck(_deck); else _nav.ToLibrary(); });

        Load();
    }

    public ObservableCollection<QuizOption> Options { get; } = new();
    public ObservableCollection<OrderItem> OrderItems { get; } = new();
    public ObservableCollection<MatchRow> MatchRows { get; } = new();

    public RelayCommand PickCommand { get; }
    public RelayCommand SubmitCommand { get; }
    public RelayCommand SubmitMultiCommand { get; }
    public RelayCommand SubmitOrderCommand { get; }
    public RelayCommand SubmitMatchCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand ExitCommand { get; }

    public QuizQuestion? Current => _index < _questions.Count ? _questions[_index] : null;

    public string Title => _title;
    public string Prompt => Current?.Prompt ?? "";
    public CardKind Kind => Current?.Card.Kind ?? CardKind.Qa;
    public string KindLabel => Kind switch
    {
        CardKind.MultipleChoice => "Pick one answer",
        CardKind.MultiSelect => "Select every correct option",
        CardKind.FillBlanks => "Fill the blank",
        CardKind.Order => "Put these in order",
        CardKind.Match => "Match each item",
        CardKind.TrueFalse => "True or false",
        CardKind.Image => "Name what is shown",
        _ => Current?.IsChoice == true ? "Choose the answer" : "Type the answer"
    };

    public bool IsChoice => Kind is CardKind.MultipleChoice or CardKind.Qa or CardKind.Image or CardKind.TrueFalse
                            && Current?.IsChoice == true;
    public bool IsTyped => Kind is CardKind.Qa or CardKind.Image or CardKind.FillBlanks && Current?.IsChoice != true;
    public bool IsMultiSelect => Kind == CardKind.MultiSelect;
    public bool IsOrder => Kind == CardKind.Order;
    public bool IsMatch => Kind == CardKind.Match;
    public bool HasImage => !string.IsNullOrWhiteSpace(Current?.Card.ImagePath);

    public string ImagePath => Current?.Card.ImagePath ?? "";
    public string SourceText => Current?.Card.Source ?? "";
    public bool HasSource => !string.IsNullOrWhiteSpace(Current?.Card.Source);

    public string CorrectAnswerText => Current switch
    {
        null => "",
        { Kind: CardKind.MultiSelect } q => string.Join(", ", q.Card.CorrectChoices
            .Where(i => i >= 0 && i < q.Card.Choices.Count)
            .Select(i => q.Card.Choices[i])),
        { Kind: CardKind.Order } q => string.Join(" → ", q.Card.Items),
        { Kind: CardKind.Match } q => string.Join("  ·  ", q.Card.Pairs.Select(p => $"{p.Left} — {p.Right}")),
        var q => q.Card.Back
    };

    public bool Answered
    {
        get => _answered;
        private set
        {
            if (!Set(ref _answered, value)) return;
            PickCommand.RaiseCanExecuteChanged();
            SubmitCommand.RaiseCanExecuteChanged();
            SubmitMultiCommand.RaiseCanExecuteChanged();
            SubmitOrderCommand.RaiseCanExecuteChanged();
            SubmitMatchCommand.RaiseCanExecuteChanged();
            MoveUpCommand.RaiseCanExecuteChanged();
            MoveDownCommand.RaiseCanExecuteChanged();
            NextCommand.RaiseCanExecuteChanged();
            Raise(nameof(FeedbackTitle));
            Raise(nameof(FeedbackDetail));
            Raise(nameof(IsCorrectAnswer));
            Raise(nameof(IsWrongAnswer));
        }
    }

    public bool WasCorrect
    {
        get => _wasCorrect;
        private set => Set(ref _wasCorrect, value);
    }

    public string TypedInput
    {
        get => _typedInput;
        set
        {
            if (!Set(ref _typedInput, value)) return;
            Notice = "";
            SubmitCommand.RaiseCanExecuteChanged();
        }
    }

    public string ScoreText => _score.ToString();
    public int Position => Math.Min(_index + 1, Math.Max(1, _questions.Count));
    public int Total => _questions.Count;
    public string ProgressText => $"{Position} of {Total}";
    public double Progress => Total == 0 ? 0 : _index * 100.0 / Total;

    public bool IsCorrectAnswer => Answered && WasCorrect;
    public bool IsWrongAnswer => Answered && !WasCorrect;
    public string FeedbackTitle => WasCorrect ? "Correct" : "Not quite";
    public string FeedbackDetail => CorrectAnswerText;

    /// Set when the reader tries to advance without answering. Cleared by any answer.
    public string Notice
    {
        get => _notice;
        private set
        {
            if (!Set(ref _notice, value)) return;
            Raise(nameof(HasNotice));
        }
    }

    public bool HasNotice => _notice.Length > 0;

    public void Pick(QuizOption option)
    {
        if (Answered || Current is null) return;

        if (IsMultiSelect)
        {
            option.Selected = !option.Selected;
            Notice = "";
            return;
        }

        if (!IsChoice) return;

        var picked = Array.IndexOf(Current.Options, option.Text);
        var correct = picked == Current.CorrectIndex;

        for (var i = 0; i < Options.Count; i++)
        {
            var isCorrect = i == Current.CorrectIndex;
            Options[i].State = isCorrect ? "correct" : (i == picked ? "wrong" : "muted");
        }

        Record(correct);
    }

    public void SubmitTyped()
    {
        if (Answered || Current is null) return;

        if (TypedInput.Trim().Length == 0)
        {
            Notice = "Type your answer first.";
            return;
        }

        Record(TextMatch.Matches(TypedInput, Current.Card.Accepted));
    }

    public void SubmitMulti()
    {
        if (Answered || Current is null) return;

        var chosen = Options.Select((o, i) => (o, i)).Where(x => x.o.Selected).Select(x => x.i).ToHashSet();
        if (chosen.Count == 0)
        {
            Notice = "Tick at least one option before checking.";
            return;
        }

        var correct = Current.CorrectIndexes.ToHashSet();

        for (var i = 0; i < Options.Count; i++)
        {
            var isCorrect = correct.Contains(i);
            var picked = chosen.Contains(i);
            Options[i].State = isCorrect && picked ? "correct"
                : isCorrect ? "missed"
                : picked ? "wrong"
                : "idle";
        }

        Record(correct.SetEquals(chosen));
    }

    public void SubmitOrder()
    {
        if (Answered || Current is null) return;

        var expected = Current.Card.Items.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray();

        // The row arrives shuffled and is never already in sequence, so no moves means
        // no answer has been given yet.
        if (expected.Length > 1 && !_orderTouched)
        {
            Notice = "Move the items into order before checking.";
            return;
        }

        var actual = OrderItems.Select(o => o.Text).ToArray();
        var correct = expected.SequenceEqual(actual);

        for (var i = 0; i < OrderItems.Count; i++)
        {
            var matches = i < expected.Length && OrderItems[i].Text == expected[i];
            OrderItems[i].State = correct ? "correct" : matches ? "idle" : "wrong";
        }

        Record(correct);
    }

    public void SubmitMatch()
    {
        if (Answered || Current is null) return;

        if (MatchRows.Any(r => string.IsNullOrWhiteSpace(r.SelectedRight)))
        {
            Notice = "Match every item before checking.";
            return;
        }

        var pairs = Current.Card.Pairs;
        var correct = true;

        for (var i = 0; i < MatchRows.Count && i < pairs.Count; i++)
        {
            var right = string.Equals(MatchRows[i].SelectedRight, pairs[i].Right, StringComparison.Ordinal);
            MatchRows[i].State = right ? "correct" : "wrong";
            if (!right) correct = false;
        }

        Record(correct);
    }

    public void Move(OrderItem? item, int delta)
    {
        if (Answered || item is null) return;

        var from = OrderItems.IndexOf(item);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= OrderItems.Count) return;

        OrderItems.Move(from, to);
        _orderTouched = true;
        Notice = "";
    }

    public void Next()
    {
        if (!Answered)
        {
            Notice = Kind switch
            {
                CardKind.MultiSelect => "Select the correct options, then check your answer.",
                CardKind.Order => "Move the items into order, then check your answer.",
                CardKind.Match => "Match every item, then check your answer.",
                _ => "Answer this question before moving on."
            };
            return;
        }

        Notice = "";
        _index++;
        Answered = false;
        TypedInput = "";

        if (_index >= _questions.Count)
        {
            Finish();
            return;
        }

        Load();
    }

    private void Load()
    {
        Options.Clear();
        OrderItems.Clear();
        MatchRows.Clear();
        Notice = "";
        _orderTouched = false;

        var current = Current;
        if (current is null) return;

        for (var i = 0; i < current.Options.Length; i++)
            Options.Add(new QuizOption(i, current.Options[i]));

        foreach (var item in current.Shuffled)
            OrderItems.Add(new OrderItem(item));

        foreach (var pair in current.Card.Pairs)
            MatchRows.Add(new MatchRow(pair.Left, current.Rights));

        Raise(nameof(Current));
        Raise(nameof(Prompt));
        Raise(nameof(Kind));
        Raise(nameof(KindLabel));
        Raise(nameof(IsChoice));
        Raise(nameof(IsTyped));
        Raise(nameof(IsMultiSelect));
        Raise(nameof(IsOrder));
        Raise(nameof(IsMatch));
        Raise(nameof(HasImage));
        Raise(nameof(ImagePath));
        Raise(nameof(CorrectAnswerText));
        Raise(nameof(HasSource));
        Raise(nameof(SourceText));
        Raise(nameof(Position));
        Raise(nameof(ProgressText));
        Raise(nameof(Progress));
        Raise(nameof(ScoreText));
    }

    private void Record(bool correct)
    {
        if (Current is null) return;

        if (correct) _score++;
        _results.Add((Current.Card, correct));

        Srs.Apply(Current.Card, correct ? Services.Grade.Good : Services.Grade.Again, DateTime.Now);
        Store.Instance.LogReview();
        _nav.RefreshHeader();

        WasCorrect = correct;
        Answered = true;
        Notice = "";
        Raise(nameof(ScoreText));
    }

    private void Finish()
    {
        Store.Instance.Save();

        var seconds = (int)Math.Max(1, (DateTime.Now - _started).TotalSeconds);
        var missed = _results.Where(r => !r.Correct).Select(r => r.Card.Front).Take(6).ToList();

        _nav.ToSummary(new SessionResult
        {
            Title = _title,
            Kind = "Quiz",
            Total = _results.Count,
            Correct = _results.Count(r => r.Correct),
            Seconds = seconds,
            Missed = missed,
            Cards = _results.Select(r => r.Card).ToList()
        });
    }
}
