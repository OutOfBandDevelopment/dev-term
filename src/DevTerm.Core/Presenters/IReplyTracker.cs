namespace DevTerm.Core.Presenters;

/// <summary>
/// Correlates an outbound query command with the next complete reply line back over the transport:
/// a control surface calls <see cref="QuerySent"/> right before sending a query so the presenter
/// (see <see cref="LineReplyPresenter"/>) knows which indicator id the next line belongs to. The
/// synchronous, one-command-at-a-time case — see docs/design/device-control-modules.md.
/// </summary>
public interface IReplyTracker
{
    void QuerySent(string replyIndicatorId);
}
