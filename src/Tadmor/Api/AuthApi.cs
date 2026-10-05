using Tadmor.Services;

namespace Tadmor.Api;

/// <summary>
/// Sessions over the JSON API (spec/api.md §3). Every /api/ path except
/// login and logout needs a live session, so the check wraps the whole API
/// and an unknown path without one is a 401, not a 404.
/// </summary>
internal static class AuthApi
{
    public const string Cookie = "tadmor_session";
    private const string UserKey = "tadmor.user";

    public static CurrentUser User(this HttpContext ctx) => (CurrentUser)ctx.Items[UserKey]!;

    /// <summary>403 unless the caller is an administrator.</summary>
    public static void RequireAdmin(this HttpContext ctx)
    {
        if (!ctx.User().IsAdmin)
        {
            throw ServiceException.Forbidden();
        }
    }

    /// <summary>Resolves the session cookie for every request; /api/ requires one.</summary>
    public static async Task Middleware(HttpContext ctx, RequestDelegate next)
    {
        var auth = ctx.RequestServices.GetRequiredService<Auth>();
        var user = await auth.LookupAsync(ctx.Request.Cookies[Cookie]);
        if (user is not null)
        {
            ctx.Items[UserKey] = user;
        }
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/api") && user is null
            && !path.Equals("/api/auth/login") && !path.Equals("/api/auth/logout"))
        {
            await Http.Error(401, "not signed in").ExecuteAsync(ctx);
            return;
        }
        await next(ctx);
    }

    public static void SetCookie(HttpContext ctx, string token) =>
        ctx.Response.Cookies.Append(Cookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = IsHttps(ctx),
            Path = "/",
            MaxAge = Auth.Lifetime,
        });

    public static void ClearCookie(HttpContext ctx) =>
        ctx.Response.Cookies.Delete(Cookie, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = IsHttps(ctx), Path = "/",
        });

    private static bool IsHttps(HttpContext ctx) =>
        ctx.Request.IsHttps
        || string.Equals(ctx.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);

    private static object UserJson(CurrentUser u) => new { u.Id, u.Email, u.FullName, u.IsAdmin };

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/auth/login", async (HttpContext ctx, Auth auth) =>
        {
            var (user, token) = await auth.LoginAsync(await Http.Body(ctx));
            SetCookie(ctx, token);
            return Http.Ok(UserJson(user));
        });
        api.MapPost("/auth/logout", async (HttpContext ctx, Auth auth) =>
        {
            await auth.LogoutAsync(ctx.Request.Cookies[Cookie]);
            ClearCookie(ctx);
            return Results.NoContent();
        });
        api.MapGet("/auth/me", (HttpContext ctx) => Http.Ok(UserJson(ctx.User())));

        var users = api.MapGroup("/users").AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.RequireAdmin();
            return await next(context);
        });
        users.MapGet("", async (Users s) => Http.Ok(await s.ListAsync()));
        users.MapGet("/{id}", async (string id, Users s) => Http.Ok(await s.GetAsync(Http.Id(id))));
        users.MapPost("", async (HttpContext ctx, Users s) =>
            Http.Created(new { id = await s.CreateAsync(await Http.Body(ctx)) }));
        users.MapPut("/{id}", async (string id, HttpContext ctx, Users s) =>
        {
            var key = Http.Id(id);
            await s.UpdateAsync(key, await Http.Body(ctx), ctx.User());
            return Results.NoContent();
        });
        users.MapPost("/{id}/password", async (string id, HttpContext ctx, Users s) =>
        {
            var key = Http.Id(id);
            await s.ResetPasswordAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });
    }
}
