namespace DevTerm.Core.Presenters;

/// <summary>
/// Turns a raw byte stream into a viewable representation. The text/numeric-base
/// presenters implement this directly; richer presenter kinds (rendering, composite,
/// mappable) build on top of the same contract.
/// </summary>
public interface IPresenter
{
    /// <summary>Stable, unique name used to select this presenter (e.g. "hex", "ascii").</summary>
    string Name { get; }

    /// <remarks>
    /// Takes <see cref="ReadOnlyMemory{T}"/> rather than <see cref="ReadOnlySpan{T}"/> so the
    /// method stays mockable (Moq/Castle cannot proxy a ref struct parameter); implementations
    /// that only need synchronous, allocation-free access can use <c>data.Span</c> internally.
    /// </remarks>
    string Render(ReadOnlyMemory<byte> data);
}

/// <summary>
/// Optional companion to <see cref="IPresenter"/> for presenters that can also turn
/// user-facing input back into bytes to send.
/// </summary>
public interface IPresenterInput
{
    byte[] Parse(string input);
}
