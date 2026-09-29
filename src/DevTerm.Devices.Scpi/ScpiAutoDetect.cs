using System.Globalization;
using System.Text;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;

namespace DevTerm.Devices.Scpi;

/// <summary>What <see cref="ScpiAutoDetect.DetectAsync"/> found.</summary>
public enum ScpiAutoDetectOutcome
{
    /// <summary>The <c>*IDN?</c> reply matched a loaded profile's <c>IdnPattern</c>.</summary>
    Matched,

    /// <summary>A reply arrived but no loaded profile recognizes it.</summary>
    Unrecognized,

    /// <summary>No reply within the timeout.</summary>
    NoReply,

    /// <summary>No "scpi" presenter is available to correlate a reply through, so nothing was sent.</summary>
    NoPresenter,
}

/// <param name="Profile">The matched profile, or <see langword="null"/> for every outcome but <see cref="ScpiAutoDetectOutcome.Matched"/>.</param>
/// <param name="Reply">The raw <c>*IDN?</c> reply, when one arrived.</param>
public sealed record ScpiAutoDetectResult(ScpiAutoDetectOutcome Outcome, ScpiInstrumentProfile? Profile, string? Reply)
{
    /// <summary>A one-line, user-facing summary - shown in the main window's output after a detect.</summary>
    public string Describe(TimeSpan timeout) => Outcome switch
    {
        ScpiAutoDetectOutcome.Matched => $"Detected {Profile!.Name} (*IDN? replied \"{Reply}\").",
        ScpiAutoDetectOutcome.Unrecognized => $"No loaded SCPI profile recognizes *IDN? reply \"{Reply}\" — opening the Generic panel.",
        ScpiAutoDetectOutcome.NoReply => $"No *IDN? reply within {ScpiAutoDetect.FormatSeconds(timeout)} — opening the Generic panel.",
        _ => "SCPI auto-detect needs the \"scpi\" presenter — opening the Generic panel.",
    };
}

/// <summary>
/// Honestly-scoped SCPI auto-detect, shared by the TUI and WPF "SCPI Instrument... > Auto-detect"
/// choice: there's no universal "list supported commands" SCPI query, so this sends <c>*IDN?</c> and
/// regex-matches the reply against each loaded profile's <c>IdnPattern</c>
/// (<see cref="ScpiProfileCatalog.TryMatchByIdn"/>), waiting up to a configurable timeout
/// (<c>CliOptions.ScpiAutoDetectTimeoutMs</c>, 3 s by default) rather than a fixed 3 s.
/// </summary>
public static class ScpiAutoDetect
{
    private const string _detectReplyId = "scpiAutoDetect.reply";

    /// <summary>
    /// Sends <c>*IDN?</c> and waits for the reply. A send failure propagates (the session has then
    /// disconnected itself and reported why).
    /// </summary>
    public static async Task<ScpiAutoDetectResult> DetectAsync(Session session, IPresenter? presenter, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (presenter is not IScpiReplyTracker tracker || presenter is not IStructuredPresenter structured)
        {
            return new ScpiAutoDetectResult(ScpiAutoDetectOutcome.NoPresenter, null, null);
        }

        var replyReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnValuesChanged(object? _, IReadOnlyDictionary<string, string> values)
        {
            if (values.TryGetValue(_detectReplyId, out var reply))
            {
                replyReceived.TrySetResult(reply);
            }
        }

        structured.ValuesChanged += OnValuesChanged;
        try
        {
            tracker.QuerySent(_detectReplyId);
            try
            {
                await session.SendAsync(Encoding.ASCII.GetBytes("*IDN?\n")).ConfigureAwait(false);
            }
            catch
            {
                // The send failed, so the id QuerySent just registered will never get a reply -
                // cancel it or it shifts every later reply onto the wrong field. See
                // docs/bugs/fixed/006-reply-queue-desync.md.
                tracker.Cancel(_detectReplyId);
                throw;
            }

            var winner = await Task.WhenAny(replyReceived.Task, Task.Delay(timeout)).ConfigureAwait(false);
            if (winner != replyReceived.Task)
            {
                // No reply within the timeout - same reasoning as the send-failure case above.
                tracker.Cancel(_detectReplyId);
                return new ScpiAutoDetectResult(ScpiAutoDetectOutcome.NoReply, null, null);
            }

            var idn = await replyReceived.Task.ConfigureAwait(false);
            return ScpiProfileCatalog.TryMatchByIdn(idn) is { } profile
                ? new ScpiAutoDetectResult(ScpiAutoDetectOutcome.Matched, profile, idn)
                : new ScpiAutoDetectResult(ScpiAutoDetectOutcome.Unrecognized, null, idn);
        }
        finally
        {
            structured.ValuesChanged -= OnValuesChanged;
        }
    }

    /// <summary>The "in progress" line shown while <see cref="DetectAsync"/> waits.</summary>
    public static string ProgressMessage(TimeSpan timeout) =>
        $"Auto-detecting the SCPI instrument: sent *IDN?, waiting up to {FormatSeconds(timeout)}…";

    internal static string FormatSeconds(TimeSpan timeout) =>
        string.Create(CultureInfo.InvariantCulture, $"{timeout.TotalSeconds:0.#} s");
}
