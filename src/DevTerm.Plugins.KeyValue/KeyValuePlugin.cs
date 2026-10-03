using System.Buffers;
using System.Text;
using DevTerm.Core.Plugins;
using DevTerm.Core.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Plugins.KeyValue;

/// <summary>An example plugin: one presenter, "keyvalue", that decodes <c>name=value [unit]</c> lines into a <see cref="StructuredMessage"/>.</summary>
public sealed class KeyValuePlugin : IPluginModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddTransient<IPresenter, KeyValuePresenter>();
}

/// <summary>
/// Line-buffers ASCII and, for each complete line of <c>name=value</c> tokens (<c>temp=21.50 C volts=3.30 V state=OK</c>; a token
/// without <c>=</c> after a field is that field's unit), raises <see cref="MessageDecoded"/> and renders one <c>name = value unit</c> row
/// per field. Lines with no <c>name=value</c> token are ignored. Try it against the loopback device's <c>STATUS?</c>.
/// </summary>
public sealed class KeyValuePresenter : IPresenter, IStructuredMessageSource
{
    private const int _maxLine = 4096;
    private readonly StringBuilder _line = new();

    public string Name => "keyvalue";

    public event EventHandler<StructuredMessage>? MessageDecoded;

    public IReadOnlyList<string> Render(ReadOnlySequence<byte> data)
    {
        var output = new List<string>();
        foreach (var segment in data)
        {
            foreach (var b in segment.Span)
            {
                if (b is (byte)'\n' or (byte)'\r')
                {
                    Complete(output);
                }
                else if (_line.Length < _maxLine)
                {
                    _line.Append((char)b);
                }
            }
        }

        return output;
    }

    /// <summary>Parses one line; null when it holds no <c>name=value</c> token.</summary>
    public static StructuredMessage? Parse(string line)
    {
        var fields = new List<StructuredField>();
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = token.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                fields.Add(new StructuredField(token[..equals], token[(equals + 1)..]));
            }
            else if (fields.Count > 0 && fields[^1].Unit is null)
            {
                fields[^1] = fields[^1] with { Unit = token };
            }
        }

        return fields.Count == 0 ? null : new StructuredMessage("keyvalue", fields);
    }

    private void Complete(List<string> output)
    {
        var line = _line.ToString();
        _line.Clear();
        if (Parse(line) is not { } message)
        {
            return;
        }

        MessageDecoded?.Invoke(this, message);
        output.AddRange(message.Fields.Select(f => f.Unit is { Length: > 0 } ? $"{f.Name} = {f.Value} {f.Unit}" : $"{f.Name} = {f.Value}"));
    }
}
