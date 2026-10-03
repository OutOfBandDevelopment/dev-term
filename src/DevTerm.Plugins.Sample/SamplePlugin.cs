using System.Buffers;
using DevTerm.Core.Plugins;
using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Plugins.Sample;

/// <summary>The smallest possible plugin: one presenter, "sample", that reports how many bytes each read held.</summary>
public sealed class SamplePlugin : IPluginModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddTransient<IPresenter, ByteCountPresenter>();
}

/// <summary>Renders each read as <c>sample: N bytes</c>.</summary>
public sealed class ByteCountPresenter : IPresenter
{
    public string Name => "sample";

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data) => [$"sample: {data.Length} bytes"];
}
