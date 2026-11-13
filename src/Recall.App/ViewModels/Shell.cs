using System.IO;
using Recall.Core;
using Recall.Models;
using Recall.Services;

namespace Recall.ViewModels;

public interface INavigator
{
    void ToLibrary();
    void ToDeck(Deck deck);
    void ToStudy(Deck? deck, IReadOnlyList<Card> cards, bool quiz);
    void ToSummary(SessionResult result);
    void ToImport();
    void ToSettings();
    void BackFromSettings();

    /// <summary>Re-reads the streak and due counters in the title bar.</summary>
    void RefreshHeader();
}

public sealed class Shell : Observable, INavigator
{
    private readonly Store _store = Store.Instance;
    private object? _current;
    private Deck? _deck;

    /// Whatever was on screen when Settings was opened, put back on the way out.
    private object? _beforeSettings;

    public Shell() => ToLibrary();

    public object? Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public string StreakText => _store.Streak().ToString();
    public string DueText => _store.DueTotal.ToString();
    public bool HasDue => _store.DueTotal > 0;

    public RelayCommand HomeCommand => new(ToLibrary);

    public RelayCommand SettingsCommand => new(ToSettings);

    public void ToLibrary()
    {
        _deck = null;
        Current = new LibraryViewModel(this);
        RefreshHeader();
    }

    public void ToDeck(Deck deck)
    {
        _deck = deck;
        Current = new DeckViewModel(this, deck);
        RefreshHeader();
    }

    public void ToStudy(Deck? deck, IReadOnlyList<Card> cards, bool quiz)
    {
        _deck = deck;
        var title = deck?.Name ?? "All decks";
        Current = quiz
            ? new QuizViewModel(this, deck, cards, title)
            : new StudyViewModel(this, deck, cards, title);
        RefreshHeader();
    }

    public void ToSummary(SessionResult result)
    {
        Current = new SummaryViewModel(this, result, _deck);
        RefreshHeader();
    }

    public void ToImport()
    {
        Current = new ImportViewModel(this);
        RefreshHeader();
    }

    public void ToSettings()
    {
        // Settings is a detour, not a destination: keep the screen that was open so
        // leaving Settings puts the user back where they were, session and all.
        if (Current is not SettingsViewModel) _beforeSettings = Current;

        Current = new SettingsViewModel(this);
        RefreshHeader();
    }

    public void BackFromSettings()
    {
        var previous = _beforeSettings;
        _beforeSettings = null;

        Current = previous ?? new LibraryViewModel(this);
        RefreshHeader();
    }

    public void RefreshHeader()
    {
        Raise(nameof(StreakText));
        Raise(nameof(DueText));
        Raise(nameof(HasDue));
    }
}
