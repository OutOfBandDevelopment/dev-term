namespace DevTerm.Core.Presenters;

/// <summary>How one received line relates to a pending query's reply id (see <see cref="LineReplyPresenter"/>).</summary>
public enum ReplyMatch
{
    /// <summary>The query declares no reply pattern: it takes the next line that no pattern-declaring query claims.</summary>
    NextLine,

    /// <summary>The query declares a reply pattern and the line matches it.</summary>
    Matches,

    /// <summary>The query declares a reply pattern and the line doesn't match: the line is not its reply.</summary>
    Rejects,
}
