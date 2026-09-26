namespace DevTerm.Wpf;

/// <summary>Where a line in <see cref="MainWindow"/>'s output list came from - drives its styling.</summary>
public enum OutputKind
{
    /// <summary>Decoded device output (<c>[presenter] text</c>) - normal text.</summary>
    Device,

    /// <summary>An app status line (connected, disconnected, switched...) - dimmed italic.</summary>
    Status,

    /// <summary>An error (a failed connect, a lost connection, rejected input...) - dark red.</summary>
    Error,
}

/// <summary>
/// One line in <see cref="MainWindow.OutputList"/>. <see cref="ToString"/> returns the text, so
/// anything reading the list as strings (copying, tests) still sees exactly what's shown.
/// </summary>
public sealed record OutputLine(string Text, OutputKind Kind)
{
    public override string ToString() => Text;
}
