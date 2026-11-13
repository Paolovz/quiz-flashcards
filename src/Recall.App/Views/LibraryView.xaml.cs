using System.Windows;
using System.Windows.Controls;
using Recall.ViewModels;

namespace Recall.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        Loaded += RestoreScroll;
        Unloaded += SaveScroll;
    }

    /// <summary>
    /// The shell swaps the whole view out when Settings opens, so the offset is kept on
    /// the view model and put back on return. Coming back should land where you left.
    /// </summary>
    private void RestoreScroll(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm) return;

        // The extent is not measured yet at Loaded, so the offset is applied once the
        // first layout pass has run.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => Page.ScrollToVerticalOffset(vm.ScrollOffset)));
    }

    private void SaveScroll(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel vm)
            vm.ScrollOffset = Page.VerticalOffset;
    }
}
