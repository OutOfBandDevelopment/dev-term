using System.Buffers;

namespace DevTerm.Core.Presenters;

/// <summary>
/// Helpers for presenters consuming a <see cref="ReadOnlySequence{T}"/> without forcing a copy
/// in the common case.
/// </summary>
public static class PresenterDataExtensions
{
    /// <summary>
    /// Returns the sequence's bytes as one contiguous span. Zero-copy when the sequence is a
    /// single segment — the common case for typical transport read sizes; copies into a new
    /// array only when the sequence spans multiple segments.
    /// </summary>
    public static ReadOnlySpan<byte> ToContiguousSpan(this in ReadOnlySequence<byte> data) =>
        data.IsSingleSegment ? data.FirstSpan : data.ToArray();
}
