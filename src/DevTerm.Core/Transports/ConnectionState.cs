namespace DevTerm.Core.Transports;

public enum ConnectionState
{
    Closed,
    Opening,
    Open,
    Closing,
    Faulted,
}
