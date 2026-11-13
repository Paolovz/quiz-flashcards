using System.Windows.Controls;
using System.Windows.Input;
using Recall.ViewModels;

namespace Recall.Views;

public partial class StudyView : UserControl
{
    public StudyView()
    {
        InitializeComponent();
        Loaded += (_, _) => Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (DataContext is not StudyViewModel vm) return;

        var key = e.Key;
        var handled = true;

        if (key is Key.Space or Key.Enter)
        {
            if (vm.Revealed) vm.GoodCommand.Execute(null);
            else vm.RevealCommand.Execute(null);
        }
        else if (key is Key.D1 or Key.NumPad1)
        {
            if (vm.Revealed) vm.AgainCommand.Execute(null);
        }
        else if (key is Key.D2 or Key.NumPad2)
        {
            if (vm.Revealed) vm.HardCommand.Execute(null);
        }
        else if (key is Key.D3 or Key.NumPad3)
        {
            if (vm.Revealed) vm.GoodCommand.Execute(null);
        }
        else if (key is Key.D4 or Key.NumPad4)
        {
            if (vm.Revealed) vm.EasyCommand.Execute(null);
        }
        else
        {
            handled = false;
        }

        if (handled) e.Handled = true;
    }
}
