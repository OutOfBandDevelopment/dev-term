namespace DevTerm.Web;

/// <summary>Settings for the web host, bound from the <c>Web</c> configuration section (env <c>DEVTERM_Web__Token</c>, ...). See docs/design/proposals/web-tunnel-blazor-frontend.md.</summary>
public sealed class WebOptions
{
    public const string SectionName = "Web";

    /// <summary>Where Kestrel listens. Loopback only unless <see cref="AllowRemote"/> is set.</summary>
    public string Urls { get; set; } = "http://127.0.0.1:5080";

    /// <summary>Shared access token. When empty a random one is generated at startup and printed once.</summary>
    public string? Token { get; set; }

    /// <summary>Permit a non-loopback bind. Also requires a token and a certificate.</summary>
    public bool AllowRemote { get; set; }

    /// <summary>A PFX certificate for HTTPS; required for any non-loopback bind.</summary>
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    /// <summary>Lines of output kept and replayed to a viewer that connects later.</summary>
    public int BacklogLines { get; set; } = 500;
}
