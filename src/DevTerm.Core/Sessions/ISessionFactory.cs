using DevTerm.Core.Presenters;
using DevTerm.Core.Transports;

namespace DevTerm.Core.Sessions;

public interface ISessionFactory
{
    Session Create(ITransport transport, Pipeline pipeline);
}

public sealed class SessionFactory : ISessionFactory
{
    public Session Create(ITransport transport, Pipeline pipeline) => new(transport, pipeline);
}
