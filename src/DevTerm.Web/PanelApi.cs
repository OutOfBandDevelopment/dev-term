namespace DevTerm.Web;

/// <summary>Body of <c>POST /api/invoke</c>: a control panel command id and its string value (see <see cref="DevTerm.Core.Control.IControlSurface"/>).</summary>
public sealed record InvokeRequest(string CommandId, string? Value);

internal static class PanelApi
{
    public static async Task<IResult> InvokeAsync(HttpContext context, DevTerm.Core.Control.IControlSurface surface, InvokeRequest request)
    {
        if (context.Items.ContainsKey(AccessTokenMiddleware.ReadOnlyItem))
        {
            return Results.Json(new { error = "read-only viewer: controls are not allowed." }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.CommandId))
        {
            return Results.BadRequest(new { error = "commandId is required." });
        }

        try
        {
            await surface.InvokeAsync(request.CommandId, request.Value, context.RequestAborted);
            return Results.Ok(new { ok = true });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
