namespace DevTerm.Transports.Rfc2217;

/// <summary>Raw Telnet protocol byte constants (RFC 854) needed to carry RFC 2217's COM-PORT-OPTION.</summary>
internal static class Telnet
{
    public const byte Iac = 0xFF;
    public const byte Will = 0xFB;
    public const byte Wont = 0xFC;
    public const byte Do = 0xFD;
    public const byte Dont = 0xFE;
    public const byte Sb = 0xFA;
    public const byte Se = 0xF0;

    /// <summary>RFC 2217's assigned Telnet option number.</summary>
    public const byte ComPortOption = 44;

    /// <summary>Builds the client's opening offer for one Telnet option: <c>IAC WILL &lt;option&gt; IAC DO &lt;option&gt;</c>.</summary>
    public static byte[] BuildWillDo(byte option) => [Iac, Will, option, Iac, Do, option];
}
