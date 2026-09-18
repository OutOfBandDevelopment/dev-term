using System.ComponentModel;

namespace DevTerm.Configuration;

/// <summary>
/// One presenter in the Connection Editor's presenter picker: its name and whether it's checked.
/// A small observable item (rather than a plain string list plus a separate "selected" collection)
/// so each front end can bind a checkbox straight to <see cref="IsSelected"/> — WPF via
/// <c>{Binding IsSelected}</c>, the TUI by copying its <c>CheckBox</c> state to/from it — see
/// <see cref="ConnectionEditorViewModel.PresenterChoices"/>.
/// </summary>
public sealed class PresenterSelection(string name) : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
