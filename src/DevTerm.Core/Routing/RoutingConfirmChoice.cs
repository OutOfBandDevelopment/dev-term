namespace DevTerm.Core.Routing;

/// <summary>The answer to a confirm-flagged broker-to-device rule's prompt.</summary>
public enum RoutingConfirmChoice
{
    /// <summary>Do not send this message.</summary>
    Drop,

    /// <summary>Send this message; ask again next time.</summary>
    SendOnce,

    /// <summary>Send this and every later message from this rule for the rest of the session.</summary>
    Always,
}
