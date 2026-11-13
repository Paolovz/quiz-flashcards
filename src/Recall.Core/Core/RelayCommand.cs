using System.Windows.Input;

namespace Recall.Core;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null)
    {
        _run = run;
        _can = can;
    }

    public RelayCommand(Action run) : this(_ => run()) { }

    public RelayCommand(Action run, Func<bool> can) : this(_ => run(), _ => can()) { }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _run(parameter);

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
