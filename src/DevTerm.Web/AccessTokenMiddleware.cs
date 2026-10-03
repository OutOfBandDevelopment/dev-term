namespace DevTerm.Web;

/// <summary>
/// Requires the shared token on every request: <c>Authorization: Bearer</c>, the <c>devterm_auth</c> cookie, or a one-time
/// <c>?token=</c> that sets the cookie and redirects to the clean URL.
/// </summary>
internal sealed class AccessTokenMiddleware(RequestDelegate next, string token, string readOnlyToken)
{
    internal const string CookieName = "devterm_auth";

    /// <summary><see cref="HttpContext.Items"/> key holding <see langword="true"/> when the request authenticated with the read-only token.</summary>
    internal const string ReadOnlyItem = "devterm.readonly";

    private bool IsReadOnlyToken(string? presented) =>
        !string.IsNullOrWhiteSpace(readOnlyToken) && AccessPolicy.TokenMatches(readOnlyToken, presented);

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!AccessPolicy.OriginAllowed(request.Headers.Origin.ToString(), request.Host.Value ?? string.Empty))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var header = request.Headers.Authorization.ToString();
        var bearer = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
        var cookie = request.Cookies[CookieName];
        if (AccessPolicy.TokenMatches(token, bearer) || AccessPolicy.TokenMatches(token, cookie))
        {
            await next(context);
            return;
        }

        if (IsReadOnlyToken(bearer) || IsReadOnlyToken(cookie))
        {
            context.Items[ReadOnlyItem] = true;
            await next(context);
            return;
        }

        var query = request.Query["token"].ToString();
        if (HttpMethods.IsGet(request.Method) && (AccessPolicy.TokenMatches(token, query) || IsReadOnlyToken(query)))
        {
            context.Response.Cookies.Append(CookieName, query, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = request.IsHttps,
            });
            context.Response.Redirect(request.Path.HasValue ? request.Path.Value! : "/");
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await context.Response.WriteAsync("dev-term: access token required (open the URL printed at startup).");
    }
}
