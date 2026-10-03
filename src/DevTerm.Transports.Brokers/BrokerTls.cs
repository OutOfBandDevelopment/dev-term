using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace DevTerm.Transports.Brokers;

/// <summary>Server-certificate checking shared by the AMQP and STOMP connections.</summary>
internal static class BrokerTls
{
    /// <summary>
    /// The normal system-trust check, plus (when <see cref="BrokerTransportOptions.TlsCaCertificatePath"/> is set) acceptance of a
    /// chain that ends at that CA. A name mismatch or any other error is never waved through.
    /// </summary>
    public static RemoteCertificateValidationCallback Validator(BrokerTransportOptions options)
    {
        X509Certificate2? ca = string.IsNullOrWhiteSpace(options.TlsCaCertificatePath) ? null : X509CertificateLoader.LoadCertificateFromFile(options.TlsCaCertificatePath);
        return (_, certificate, _, errors) =>
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            if (ca is null || certificate is null || errors != SslPolicyErrors.RemoteCertificateChainErrors)
            {
                return false;
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(new X509Certificate2(certificate));
        };
    }
}
