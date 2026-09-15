using System.Windows.Input;

namespace DevTerm.Configuration;

/// <summary>
/// A minimal <see cref="ICommand"/> implementation shared by both front ends — <c>ICommand</c>
/// itself lives in the base class library, not WPF, so a plain class library like this one can
/// implement it without any UI framework reference. WPF's XAML <c>Command="{Binding ...}"</c>
/// bindings consume it directly; the TUI (<see cref="ConfigureMode"/>) just calls
/// <see cref="Execute"/> from a Terminal.Gui button's own <c>Action</c> — same command logic, two
/// different invocation mechanisms, since Terminal.Gui has no data-binding system of its own.
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
