using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Loopback;

public sealed class LoopbackTransportOptionsValidator : IValidateOptions<LoopbackTransportOptions>
{
    public ValidateOptionsResult Validate(string? name, LoopbackTransportOptions options) => ValidateOptionsResult.Success;
}
