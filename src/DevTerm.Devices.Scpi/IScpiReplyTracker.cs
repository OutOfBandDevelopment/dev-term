using DevTerm.Core.Presenters;

namespace DevTerm.Devices.Scpi;

/// <summary>
/// Correlates an outbound query command with the next line back over the transport, resolving
/// device-control-modules.md's "can a command declare an expected reply pattern" open question for
/// the simple, one-command-at-a-time case SCPI itself is. Implemented by <see cref="ScpiReplyPresenter"/>;
/// <see cref="ScpiControlSurface"/> calls <see cref="IReplyTracker.QuerySent"/> right before sending a query so the
/// presenter knows which indicator id the next complete line belongs to. The SCPI name for the
/// shared <see cref="IReplyTracker"/>.
/// </summary>
public interface IScpiReplyTracker : IReplyTracker;
