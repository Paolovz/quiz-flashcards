using System.Collections.ObjectModel;
using System.Windows;
using Recall.Core;
using Recall.Services;
using Recall.Theming;

namespace Recall.ViewModels;

/// <summary>A palette card in Settings. Swatch colors are carried as brushes built from
/// the option's own hex values, so a theme can be previewed without being applied.</summary>
public sealed class ThemeRow : Observable
{
    private bool _isSelected;

    public ThemeRow(ThemeOption option, bool isSelected)
    {
        Option = option;
        _isSelected = isSelected;
    }

    public ThemeOption Option { get; }

    public string Name => Option.Name;
    public string Blurb => Option.Blurb;
    public string Mode => Option.IsDark ? "Dark" : "Light";
    public string Id => Option.Id;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value)) Raise(nameof(SelectedTag));
        }
    }

    public string SelectedTag => _isSelected ? "selected" : "";

    public System.Windows.Media.Brush Background => Swatch(Option.Background);
    public System.Windows.Media.Brush Surface => Swatch(Option.Surface);
    public System.Windows.Media.Brush Primary => Swatch(Option.Primary);
    public System.Windows.Media.Brush Accent => Swatch(Option.Accent);

    private static System.Windows.Media.Brush Swatch(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class SettingsViewModel : Observable
{
    private readonly INavigator _nav;
    private readonly Store _store = Store.Instance;
    private string _status = "";

    public SettingsViewModel(INavigator nav)
    {
        _nav = nav;

        Themes = new ObservableCollection<ThemeRow>(
            ThemeManager.All.Select(t => new ThemeRow(t, t.Id == ThemeManager.Current)));

        SelectThemeCommand = new RelayCommand(p => SelectTheme(p as ThemeRow));
        CopyPathCommand = new RelayCommand(CopyPath);
        BackCommand = new RelayCommand(_nav.ToLibrary);
    }

    public ObservableCollection<ThemeRow> Themes { get; }

    public RelayCommand SelectThemeCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public RelayCommand BackCommand { get; }

    public string CurrentName => ThemeManager.CurrentOption.Name;

    public string DataPath => _store.DataPath;

    public string DeckSummary
    {
        get
        {
            var decks = _store.Data.Decks.Count;
            var cards = _store.AllCards().Count();
            return $"{decks} deck{(decks == 1 ? "" : "s")} · {cards} card{(cards == 1 ? "" : "s")}";
        }
    }

    /// One line that confirms the pick, so the click has a visible result beyond the repaint.
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

    private void SelectTheme(ThemeRow? row)
    {
        if (row is null || row.Id == ThemeManager.Current) return;

        ThemeManager.Apply(row.Id);

        foreach (var theme in Themes)
            theme.IsSelected = theme.Id == ThemeManager.Current;

        Raise(nameof(CurrentName));
        Status = $"{row.Name} applied.";
    }

    private void CopyPath()
    {
        try
        {
            Clipboard.SetText(_store.DataPath);
            Status = "Data folder path copied.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            Status = "Could not reach the clipboard. The path is above.";
        }
    }
}
