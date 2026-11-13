using System.Windows.Controls;
using System.Windows.Input;
using Recall.Models;
using Recall.ViewModels;

namespace Recall.Views;

public partial class QuizView : UserControl
{
    public QuizView()
    {
        InitializeComponent();
        Loaded += (_, _) => Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (DataContext is not QuizViewModel vm) return;

        // Typing into the answer box must keep its own keys.
        if (Keyboard.FocusedElement is TextBox && e.Key is not (Key.Enter or Key.Escape))
            return;

        var handled = true;

        if (e.Key is Key.Enter)
        {
            if (vm.Answered) vm.Next();
            else if (vm.IsTyped && vm.TypedInput.Trim().Length > 0) vm.SubmitTyped();
            else vm.Next(); // surfaces the "answer this first" notice
        }
        else if (e.Key is Key.Escape)
        {
            vm.ExitCommand.Execute(null);
        }
        else if (KeyIndex(e.Key) is { } index)
        {
            Pick(vm, index);
        }
        else
        {
            handled = false;
        }

        if (handled) e.Handled = true;
    }

    /// Accepts both the printed letter (A-D) and the number row (1-4).
    private static int? KeyIndex(Key key) => key switch
    {
        Key.A or Key.NumPad1 or Key.D1 => 0,
        Key.B or Key.NumPad2 or Key.D2 => 1,
        Key.C or Key.NumPad3 or Key.D3 => 2,
        Key.D or Key.NumPad4 or Key.D4 => 3,
        _ => null
    };

    private static void Pick(QuizViewModel vm, int index)
    {
        if (vm.Answered || index >= vm.Options.Count) return;

        // Choice questions pick by letter; multi-select toggles the same row.
        if (vm.IsChoice || vm.IsMultiSelect) vm.Pick(vm.Options[index]);
    }
}
