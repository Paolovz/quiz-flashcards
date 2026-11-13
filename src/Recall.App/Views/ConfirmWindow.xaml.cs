using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace Recall.Views;

/// One modal yes/no for an action that cannot be undone. Every delete in the app asks
/// first, and the safe answer is the one holding focus, so a stray Enter keeps the card.
public partial class ConfirmWindow : Window
{
    private ConfirmWindow(string title, string message, string confirmText, string kind)
    {
        InitializeComponent();

        KindText.Text = kind;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        AutomationProperties.SetName(ConfirmButton, confirmText);

        Loaded += (_, _) =>
        {
            CancelButton.Focus();
            Keyboard.Focus(CancelButton);
        };
    }

    /// Shows the modal over the main window. True only when the reader confirms.
    public static bool Ask(string title, string message, string confirmText = "Delete", string kind = "DELETE")
    {
        var owner = Application.Current?.MainWindow;

        var window = new ConfirmWindow(title, message, confirmText, kind);
        if (owner is null || !owner.IsLoaded)
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
                   == MessageBoxResult.Yes;

        window.Owner = owner;

        // Cover the owner's client area exactly. Owner.Left/Top include the frame, so the
        // scrim would sit a title bar too high without going through PointToScreen.
        var source = PresentationSource.FromVisual(owner);
        var toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var origin = toDip.Transform(owner.PointToScreen(new Point(0, 0)));

        window.Left = origin.X;
        window.Top = origin.Y;
        window.Width = owner.ActualWidth;
        window.Height = owner.ActualHeight;

        return window.ShowDialog() == true;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnScrim(object sender, MouseButtonEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
