using System.ComponentModel.DataAnnotations;

namespace DevTerm.Presenters.Text;

/// <summary>Configuration for <see cref="AsciiPresenter"/>. Bound via the Options pattern.</summary>
public sealed class AsciiPresenterOptions
{
    /// <summary>See <see cref="AsciiPresenter(int)"/>. <c>0</c> means unbounded (wait for a line terminator only).</summary>
    [Range(0, int.MaxValue)]
    public int MaxLineLength { get; set; } = AsciiPresenter.DefaultMaxLineLength;
}
