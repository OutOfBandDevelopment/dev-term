using System.Buffers;

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
    /// Takes a <see cref="ReadOnlySequence{T}"/> — the shape a <see cref="System.IO.Pipelines.PipeReader"/>
    /// hands back, and a plain struct so the method stays mockable (Moq/Castle cannot proxy a
    /// ref struct parameter like <see cref="ReadOnlySpan{T}"/>). Use
    /// <see cref="PresenterDataExtensions.ToContiguousSpan"/> for zero-copy access in the common
    /// single-segment case.
    /// </remarks>
    string Render(ReadOnlySequence<byte> data);
}

/// <summary>
/// Optional companion to <see cref="IPresenter"/> for presenters that can also turn
/// user-facing input back into bytes to send.
/// </summary>
public interface IPresenterInput
{
    byte[] Parse(string input);
}
