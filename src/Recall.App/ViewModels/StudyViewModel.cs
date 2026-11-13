using System.Windows.Input;
using Recall.Core;
using Recall.Models;
using Recall.Services;

namespace Recall.ViewModels;

public sealed class StudyViewModel : Observable
{
    private readonly INavigator _nav;
    private readonly Deck? _deck;
    private readonly List<Card> _queue;
    private readonly string _title;
    private readonly DateTime _started = DateTime.Now;
    private readonly List<(Card Card, bool Correct)> _graded = new();

    private int _index;
    private bool _revealed;

    public StudyViewModel(INavigator nav, Deck? deck, IReadOnlyList<Card> cards, string title)
    {
        _nav = nav;
        _deck = deck;
        _queue = cards.ToList();
        _title = title;

        RevealCommand = new RelayCommand(Reveal);
        AgainCommand = new RelayCommand(() => Grade(Services.Grade.Again), () => Revealed);
        HardCommand = new RelayCommand(() => Grade(Services.Grade.Hard), () => Revealed);
        GoodCommand = new RelayCommand(() => Grade(Services.Grade.Good), () => Revealed);
        EasyCommand = new RelayCommand(() => Grade(Services.Grade.Easy), () => Revealed);        ExitCommand = new RelayCommand(() => _nav.ToDeck(_deck!));

        if (_queue.Count == 0) Finish();
    }

    public RelayCommand RevealCommand { get; }
    public RelayCommand AgainCommand { get; }
    public RelayCommand HardCommand { get; }
    public RelayCommand GoodCommand { get; }
    public RelayCommand EasyCommand { get; }
    public RelayCommand ExitCommand { get; }

    public Card? Current => _index < _queue.Count ? _queue[_index] : null;

    public string Title => _title;
    public string FrontText => Current?.Front ?? "";
    public string BackText => Current?.Back ?? "";
    public string HintText => Current?.Hint ?? "";
    public bool HasHint => !string.IsNullOrWhiteSpace(Current?.Hint);
    public string SourceText => Current?.Source ?? "";
    public bool HasSource => !string.IsNullOrWhiteSpace(Current?.Source);

    public bool Revealed
    {
        get => _revealed;
        private set
        {
            if (!Set(ref _revealed, value)) return;
            foreach (var command in new[] { AgainCommand, HardCommand, GoodCommand, EasyCommand })
                command.RaiseCanExecuteChanged();
        }
    }

    public int Position => Math.Min(_index + 1, _queue.Count);
    public int Total => _queue.Count;
    public string ProgressText => $"{Position} of {Total}";
    public double Progress => Total == 0 ? 0 : _index * 100.0 / Total;

    public string AgainInterval => Preview(Services.Grade.Again);
    public string HardInterval => Preview(Services.Grade.Hard);
    public string GoodInterval => Preview(Services.Grade.Good);
    public string EasyInterval => Preview(Services.Grade.Easy);

    private string Preview(Services.Grade grade)
        => Current is null ? "" : Srs.PreviewInterval(Current, grade, DateTime.Now);

    private void Reveal()
    {
        if (Revealed || Current is null) return;
        Revealed = true;
    }

    public void Grade(Services.Grade grade)
    {
        if (Current is null) return;

        var card = Current;
        Srs.Apply(card, grade, DateTime.Now);
        Store.Instance.LogReview();
        _graded.Add((card, grade != Services.Grade.Again));

        _index++;
        Revealed = false;

        if (_index >= _queue.Count)
        {
            Finish();
            return;
        }

        Raise(nameof(Current));
        Raise(nameof(FrontText));
        Raise(nameof(BackText));
        Raise(nameof(HintText));
        Raise(nameof(HasHint));
        Raise(nameof(SourceText));
        Raise(nameof(HasSource));
        Raise(nameof(Position));
        Raise(nameof(ProgressText));
        Raise(nameof(Progress));
        Raise(nameof(AgainInterval));
        Raise(nameof(HardInterval));
        Raise(nameof(GoodInterval));
        Raise(nameof(EasyInterval));
    }

    private void Finish()
    {
        Store.Instance.Save();

        var seconds = (int)Math.Max(1, (DateTime.Now - _started).TotalSeconds);
        var missed = _graded.Where(g => !g.Correct).Select(g => g.Card.Front).Take(6).ToList();

        _nav.ToSummary(new SessionResult
        {
            Title = _title,
            Kind = "Study",
            Total = _graded.Count,
            Correct = _graded.Count(g => g.Correct),
            Seconds = seconds,
            Missed = missed,
            Cards = _graded.Select(g => g.Card).ToList()
        });
    }
}
