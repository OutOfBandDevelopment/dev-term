using System.Buffers;

namespace DevTerm.Presenters.Text.Tests;

/// <summary>Small helper so tests can write <c>Seq(0x01, 0x02)</c> instead of constructing a <see cref="ReadOnlySequence{T}"/> by hand everywhere.</summary>
internal static class TestSequence
{
    public static ReadOnlySequence<byte> Of(params byte[] bytes) => new(bytes);
}
