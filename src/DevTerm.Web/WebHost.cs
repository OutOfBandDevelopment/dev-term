using System.Security.Cryptography.X509Certificates;
using DevTerm.Configuration;
using DevTerm.Core.Control;
using DevTerm.Core.Presenters;
using DevTerm.Core.Sessions;
using DevTerm.Core.Transports;
using DevTerm.UiDefinitions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

namespace DevTerm.Web;

/// <summary>Builds the web host: one shared session, token auth, a <c>/ws</c> tunnel and a single-page terminal.</summary>
public static class WebHost
{
    /// <summary>The token in effect (generated when none was configured), so a caller can print the access URL.</summary>
    public sealed record Built(WebApplication App, SessionHub Hub, string Token, SessionHttpControlServer? ControlHttp = null);

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

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(WebHost).Assembly.GetName().Name }); // fixed so the static-asset manifests are found when hosted by a test host
        builder.WebHost.UseStaticWebAssets(); // otherwise only Development resolves framework assets such as blazor.web.js
        builder.Services.AddDevTermFrontEnd(cliOptions);
        builder.Services.AddSingleton(sp =>
        {
            var catalog = sp.GetRequiredService<PresenterCatalog>();
            var presenters = DevTermSessionBuilder.ResolvePresenters(catalog, cliOptions);
            var session = sp.GetRequiredService<ISessionFactory>().Create(sp.GetRequiredService<ITransport>(), new Pipeline(presenters));
            return new SessionHub(session, catalog, cliOptions, webOptions.BacklogLines);
        });

        builder.Services.AddSingleton<Components.PanelHostHolder>();
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<HostEvents>();
        builder.Services.AddSingleton(sp => new ConnectionManager(cliOptions.Project, webOptions.BacklogLines, sp.GetRequiredService<HostEvents>()));
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
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

        app.UseMiddleware<AccessTokenMiddleware>(token, webOptions.ReadOnlyToken ?? string.Empty);
        app.UseWebSockets();
        app.UseAntiforgery();
        app.Map("/ws", (HttpContext context) => WebSocketTunnel.HandleAsync(context, hub));
        app.MapStaticAssets(); // serves _framework/blazor.web.js, without which the Blazor panel never becomes interactive
        app.MapRazorComponents<Components.App>().AddInteractiveServerRenderMode();
        app.MapGet("/", () => Results.Content(TerminalPage.Html, "text/html; charset=utf-8"));
        app.MapGet("/api/status", (HttpContext context) => Results.Json(new { state = hub.State.ToString(), connection = hub.Description, readOnly = context.Items.ContainsKey(AccessTokenMiddleware.ReadOnlyItem) })).WithSummary("State and description of the shared session");

        // The REST surface as OpenAPI with a Scalar viewer, and the two streaming channels as AsyncAPI. All behind the same token.
        app.MapOpenApi("/openapi/v1.json");
        app.MapScalarApiReference("/scalar", options => options.WithTitle("dev-term web host"));
        app.MapGet("/asyncapi.json", () => Results.Content(AsyncApiDocument.Json, "application/json"));
        var events = app.Services.GetRequiredService<HostEvents>();
        var connections = app.Services.GetRequiredService<ConnectionManager>();
        hub.LineReceived += line => events.Publish("line", new { id = "main", text = line });
        app.MapGet("/api/events", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            try
            {
                await foreach (var frame in events.SubscribeAsync(context.RequestAborted))
                {
                    await context.Response.WriteAsync(frame, context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
            }
        })
            .WithMetadata(new Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof(string), StatusCodes.Status200OK, "text/event-stream"))
            .WithSummary("Server-Sent Events: connection-opened, connection-closed and line events");
        app.MapGet("/api/devices", () => Results.Json(DeviceEnumeration.Enumerate())).WithSummary("Attached serial, HID and USBTMC devices");

        // The connections of the --project file (name + description only; no credentials leave the host), or [] without one.
        app.MapGet("/api/project", () => Results.Json(connections.Project().Select(c => new { name = c.Name, description = c.Description }))).WithSummary("The project file's connections (name and description only)");

        // Create, replace and remove a project connection. The body is the profile JSON (what a saved profile holds).
        app.MapPut("/api/project/connections/{name}", async (HttpContext context, string name) =>
        {
            if (context.Items.ContainsKey(AccessTokenMiddleware.ReadOnlyItem))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            using var reader = new StreamReader(context.Request.Body);
            var error = connections.Upsert(name, await reader.ReadToEndAsync(context.RequestAborted));
            return error is null ? Results.NoContent() : Results.BadRequest(new { error });
        }).WithSummary("Create or replace a project connection from profile JSON; 400 with the reason when invalid or there is no --project");
        app.MapDelete("/api/project/connections/{name}", (HttpContext context, string name) =>
            context.Items.ContainsKey(AccessTokenMiddleware.ReadOnlyItem)
                ? Results.StatusCode(StatusCodes.Status403Forbidden)
                : connections.Remove(name) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Remove a project connection; 404 when unknown");

        // Extra connections opened from the project file, each its own session behind /ws/{id}. They use the same
        // shared host token (decided 2026-10-03); a read-only viewer cannot open or close one.
        app.MapGet("/api/connections", () => Results.Json(connections.Open().Select(c => new { id = c.Id, name = c.Name, state = c.State }))).WithSummary("The extra connections currently open");
        app.MapPost("/api/connections", async (HttpContext context, string name) =>
        {
            if (context.Items.ContainsKey(AccessTokenMiddleware.ReadOnlyItem))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return await connections.OpenAsync(name, context.RequestAborted) is { } opened
                ? Results.Json(new { id = opened.Id, name = opened.Name })
                : Results.NotFound();
        }).WithSummary("Open a project connection as its own session; 404 unknown name, 403 read-only");
        app.MapDelete("/api/connections/{id}", async (HttpContext context, string id) =>
        {
            if (context.Items.ContainsKey(AccessTokenMiddleware.ReadOnlyItem))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return await connections.CloseAsync(id) ? Results.NoContent() : Results.NotFound();
        }).WithSummary("Close an opened connection; 404 unknown id, 403 read-only");
        app.Map("/ws/{id}", (HttpContext context, string id) => connections.TryGet(id, out var found) ? WebSocketTunnel.HandleAsync(context, found) : Task.FromResult(context.Response.StatusCode = StatusCodes.Status404NotFound));
        app.Lifetime.ApplicationStopping.Register(() => connections.CloseAllAsync().GetAwaiter().GetResult());

        var (definition, surface) = webOptions.Panel?.ToLowerInvariant() switch
        {
            { } id when app.Services.GetServices<IDevicePanelContribution>().FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) is { } contributed
                => (contributed.BuildDefinition(), contributed.CreateSurface(hub.Session)),
            _ => ((UiDefinition?)null, (IControlSurface?)null),
        };
        if (!string.IsNullOrWhiteSpace(webOptions.Panel) && definition is null)
        {
            throw new InvalidOperationException($"Web:Panel '{webOptions.Panel}' must be k8055, busylight or the id of a plugin-contributed panel.");
        }

        if (definition is not null && surface is not null)
        {
            var panelJson = UiDefinitionSerializer.ToJson(definition);
            app.MapGet("/api/panel", () => Results.Content(panelJson, "application/json"));
            var holder = app.Services.GetRequiredService<Components.PanelHostHolder>();
            holder.Set(definition, surface);
            var presenterName = app.Services.GetServices<IDevicePanelContribution>().FirstOrDefault(c => string.Equals(c.Id, webOptions.Panel, StringComparison.OrdinalIgnoreCase))?.PresenterName ?? webOptions.Panel!.ToLowerInvariant();
            if (hub.Catalog.TryGet(presenterName, out var source) && source is IStructuredPresenter structured)
            {
                structured.ValuesChanged += (_, values) => holder.Publish(values);
            }

            app.MapPost("/api/invoke", (HttpContext context, InvokeRequest request) => PanelApi.InvokeAsync(context, surface, request));
        }
        else
        {
            app.MapGet("/api/panel", () => Results.NotFound());
        }

        // --controlhttp <port>: the same loopback command/event channel the console front ends offer, on the shared session.
        SessionHttpControlServer? controlHttp = null;
        if (cliOptions.ControlHttp > 0)
        {
            controlHttp = new SessionHttpControlServer(hub.Session, cliOptions.ControlHttp, text => TypedInput.TryEncode(hub.Catalog, cliOptions, text), cliOptions.ControlToken);
            var registration = hub.Session.AddObserver(controlHttp);
            app.Lifetime.ApplicationStopping.Register(() =>
            {
                registration.Dispose();
                controlHttp.DisposeAsync().AsTask().GetAwaiter().GetResult();
            });
        }

        return new Built(app, hub, token, controlHttp);
    }
}
