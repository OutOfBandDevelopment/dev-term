using System.Security.Cryptography.X509Certificates;
using DevTerm.Configuration;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DevTerm.Web;

/// <summary>Builds the web host: one shared session, token auth, a <c>/ws</c> tunnel and a single-page terminal.</summary>
public static class WebHost
{
    /// <summary>The token in effect (generated when none was configured), so a caller can print the access URL.</summary>
    public sealed record Built(WebApplication App, SessionHub Hub, string Token);

    /// <exception cref="InvalidOperationException">The options would expose the session unsafely (see <see cref="AccessPolicy.Validate"/>).</exception>
    public static Built Build(CliOptions cliOptions, WebOptions webOptions, string[] args)
    {
        ArgumentNullException.ThrowIfNull(cliOptions);
        ArgumentNullException.ThrowIfNull(webOptions);

        if (AccessPolicy.Validate(webOptions) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var token = string.IsNullOrWhiteSpace(webOptions.Token) ? AccessPolicy.GenerateToken() : webOptions.Token;

        var builder = WebApplication.CreateSlimBuilder(args);
        builder.Services.AddDevTermFrontEnd(cliOptions);
        builder.Services.AddSingleton(sp =>
        {
            var catalog = sp.GetRequiredService<PresenterCatalog>();
            var presenters = DevTermSessionBuilder.ResolvePresenters(catalog, cliOptions);
            var session = sp.GetRequiredService<ISessionFactory>().Create(sp.GetRequiredService<ITransport>(), new Pipeline(presenters));
            return new SessionHub(session, catalog, cliOptions, webOptions.BacklogLines);
        });

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            var certificate = string.IsNullOrWhiteSpace(webOptions.CertificatePath)
                ? null
                : X509CertificateLoader.LoadPkcs12FromFile(webOptions.CertificatePath, webOptions.CertificatePassword);
            foreach (var url in webOptions.Urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var uri = new Uri(url);
                var address = uri.Host == "localhost" ? System.Net.IPAddress.Loopback : System.Net.IPAddress.Parse(uri.Host.Trim('[', ']'));
                kestrel.Listen(address, uri.Port, listen =>
                {
                    if (uri.Scheme == "https" && certificate is not null)
                    {
                        listen.UseHttps(certificate);
                    }
                });
            }
        });

        var app = builder.Build();
        var hub = app.Services.GetRequiredService<SessionHub>();

        app.UseMiddleware<AccessTokenMiddleware>(token);
        app.UseWebSockets();
        app.Map("/ws", (HttpContext context) => WebSocketTunnel.HandleAsync(context, hub));
        app.MapGet("/", () => Results.Content(TerminalPage.Html, "text/html; charset=utf-8"));
        app.MapGet("/api/status", () => Results.Json(new { state = hub.State.ToString(), connection = hub.Description }));
        return new Built(app, hub, token);
    }
}
