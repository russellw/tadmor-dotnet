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
            await Http.Error(refusal.Status, refusal.Message).ExecuteAsync(ctx);
        }
    }
}
