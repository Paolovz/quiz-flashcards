using System.Collections.ObjectModel;
using Recall.Core;
using Recall.Models;

namespace Recall.ViewModels;

public sealed class SummaryViewModel : Observable
{
    private readonly INavigator _nav;
    private readonly SessionResult _result;
    private readonly Deck? _deck;

    public SummaryViewModel(INavigator nav, SessionResult result, Deck? deck)
    {
        _nav = nav;
        _result = result;
        _deck = deck;

        AgainCommand = new RelayCommand(Again);
        BackCommand = new RelayCommand(() => { if (_deck is not null) _nav.ToDeck(_deck); else _nav.ToLibrary(); });
        LibraryCommand = new RelayCommand(_nav.ToLibrary);

        foreach (var missed in result.Missed) Missed.Add(missed);
    }

    public RelayCommand AgainCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand LibraryCommand { get; }

    public ObservableCollection<string> Missed { get; } = new();

    public string Kind => _result.Kind;
    public string Title => _result.Title;
    public string AccuracyLabel => _result.AccuracyLabel;
    public double Accuracy => _result.Accuracy;
    public string DurationLabel => _result.DurationLabel;
    public string ScoreLabel => $"{_result.Correct} / {_result.Total}";

    public string Headline => _result.Accuracy switch
    {
        >= 90 => "Locked in",
        >= 70 => "Solid session",
        >= 50 => "Getting there",
        _ => "Worth another pass"
    };

    public string Body => _result.Accuracy switch
    {
        >= 90 => "Most of this is sticking. The scheduler pushed those cards out.",
        >= 70 => "Good recall. The ones you missed come back in ten minutes.",
        >= 50 => "Half the set landed. Run it again while it is fresh.",
        _ => "New material. Repetition is the whole mechanism, not a fallback."
    };

    public bool HasMissed => Missed.Count > 0;

    private void Again()
    {
        if (_result.Cards.Count == 0) return;
        _nav.ToStudy(_deck, _result.Cards, quiz: _result.Kind == "Quiz");
    }
}
