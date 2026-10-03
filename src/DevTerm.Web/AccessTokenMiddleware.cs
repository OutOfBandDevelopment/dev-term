namespace DevTerm.Web;

/// <summary>
/// Requires the shared token on every request: <c>Authorization: Bearer</c>, the <c>devterm_auth</c> cookie, or a one-time
/// <c>?token=</c> that sets the cookie and redirects to the clean URL.
/// </summary>
internal sealed class AccessTokenMiddleware(RequestDelegate next, string token)
{
    internal const string CookieName = "devterm_auth";

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
        if (AccessPolicy.TokenMatches(token, bearer) || AccessPolicy.TokenMatches(token, request.Cookies[CookieName]))
        {
            await next(context);
            return;
        }

        if (HttpMethods.IsGet(request.Method) && AccessPolicy.TokenMatches(token, request.Query["token"]))
        {
            context.Response.Cookies.Append(CookieName, token, new CookieOptions
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
