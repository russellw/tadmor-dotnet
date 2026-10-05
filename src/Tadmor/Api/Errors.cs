using Microsoft.AspNetCore.Http.Features;
using Tadmor.Services;

namespace Tadmor.Api;

/// <summary>
/// Turns refusals into the spec's error responses (spec/api.md §1.4): a
/// ServiceException carries its status, and a refusal by the schema is
/// mapped by SQLSTATE. Anything else is logged and is a 500.
/// </summary>
internal sealed class Errors(RequestDelegate next, ILogger<Errors> log)
{
    /// <summary>Where a UI page's refusal is left for the error page.</summary>
    public const string ErrorKey = "tadmor.error";

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception e) when (!ctx.Response.HasStarted)
        {
            var refusal = e as ServiceException ?? DatabaseErrors.Refusal(e);
            if (refusal is null && e is BadHttpRequestException bad)
            {
                refusal = new ServiceException(bad.StatusCode, bad.Message);
            }
            if (refusal is null)
            {
                log.LogError(e, "{Method} {Path} failed", ctx.Request.Method, ctx.Request.Path);
                refusal = new ServiceException(500, "internal server error");
            }
            ctx.Response.Clear();
            ctx.Features.Get<IHttpResponseFeature>()!.ReasonPhrase = null;
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                await Http.Error(refusal.Status, refusal.Message).ExecuteAsync(ctx);
                return;
            }
            // A UI page: show the error page in place, with the status.
            ctx.Items[ErrorKey] = refusal;
            ctx.Response.StatusCode = refusal.Status;
            ctx.SetEndpoint(null);
            ctx.Request.RouteValues.Clear();
            ctx.Request.Path = "/error";
            ctx.Request.QueryString = QueryString.Empty;
            ctx.Request.Method = HttpMethods.Get;
            await next(ctx);
        }
    }
}
