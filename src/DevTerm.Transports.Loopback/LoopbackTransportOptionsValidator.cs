using Microsoft.Extensions.Options;

namespace DevTerm.Transports.Loopback;

public sealed class LoopbackTransportOptionsValidator : IValidateOptions<LoopbackTransportOptions>
{
    public ValidateOptionsResult Validate(string? name, LoopbackTransportOptions options) =>
        options.SampleIntervalMs < 0
            ? ValidateOptionsResult.Fail("SampleIntervalMs must be 0 or greater.")
            : ValidateOptionsResult.Success;
}
