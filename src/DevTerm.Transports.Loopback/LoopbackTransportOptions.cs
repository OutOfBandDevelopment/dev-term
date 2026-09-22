namespace DevTerm.Transports.Loopback;

/// <summary>
/// Configuration for <see cref="LoopbackTransport"/>. Empty today — the transport always answers
/// with <see cref="LoopbackScript.Default"/> — but kept as a real options type (rather than no
/// options at all) so it fits the same DI shape as every other transport and can grow a
/// custom-script option later without a breaking constructor change.
/// </summary>
public sealed class LoopbackTransportOptions
{
}
