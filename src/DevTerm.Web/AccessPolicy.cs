using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DevTerm.Web;

/// <summary>The web host's access rules, kept pure so they can be tested without a server.</summary>
public static class AccessPolicy
{
    /// <summary>
    /// Returns an error message when <paramref name="options"/> would expose the session unsafely, or
    /// <see langword="null"/> when it is fine: any non-loopback URL needs AllowRemote, an explicit token, and a certificate with https.
    /// </summary>
    public static string? Validate(WebOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.ReadOnlyToken) && string.Equals(options.ReadOnlyToken, options.Token, StringComparison.Ordinal))
        {
            return "Web:ReadOnlyToken must differ from Web:Token.";
        }

        if (!string.IsNullOrWhiteSpace(options.Panel) && options.Panel.ToLowerInvariant() is not ("k8055" or "busylight"))
        {
            return $"Web:Panel '{options.Panel}' must be k8055 or busylight.";
        }

        var urls = options.Urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length == 0)
        {
            return "Web:Urls is empty.";
        }

        var anyRemote = false;
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                return $"'{url}' is not an http(s) URL.";
            }

            if (!IsLoopback(uri.Host))
            {
                anyRemote = true;
                if (uri.Scheme != "https")
                {
                    return $"'{url}' is not loopback, so it must use https.";
                }
            }
        }

        if (!anyRemote)
        {
            return null;
        }

        if (!options.AllowRemote)
        {
            return "A non-loopback bind needs Web:AllowRemote=true (the default is loopback only).";
        }

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            return "A non-loopback bind needs an explicit Web:Token; a generated one is only allowed on loopback.";
        }

        if (string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            return "A non-loopback bind needs Web:CertificatePath (a PFX) so traffic is encrypted.";
        }

        return null;
    }

    public static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    /// <summary>A fresh 256-bit token, URL-safe.</summary>
    public static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Constant-time comparison, so a wrong token leaks nothing about a right one.</summary>
    public static bool TokenMatches(string expected, string? presented) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(presented)));

    /// <summary>
    /// True when a browser's <c>Origin</c> header is absent (not a browser) or names the same host as the request,
    /// which stops another site's script from driving a loopback tunnel.
    /// </summary>
    public static bool OriginAllowed(string? origin, string requestHost) =>
        string.IsNullOrEmpty(origin)
        || (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, requestHost, StringComparison.OrdinalIgnoreCase));
}
