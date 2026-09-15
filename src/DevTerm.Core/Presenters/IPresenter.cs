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
    /// Returns zero or more complete renderings for this call. A presenter is free to buffer
    /// partial data internally (e.g. accumulating text until a line terminator, or a protocol
    /// decoder accumulating a partial frame) and emit nothing until it has something complete —
    /// or more than one item, if more than one boundary arrived within a single read. Takes a
    /// <see cref="ReadOnlySequence{T}"/> — the shape a <see cref="System.IO.Pipelines.PipeReader"/>
    /// hands back, and a plain struct so the method stays mockable (Moq/Castle cannot proxy a
    /// ref struct parameter like <see cref="ReadOnlySpan{T}"/>). Use
    /// <see cref="PresenterDataExtensions.ToContiguousSpan"/> for zero-copy access to the whole
    /// chunk in the common single-segment case.
    /// </remarks>
    IReadOnlyList<string> Render(ReadOnlySequence<byte> data);
}
