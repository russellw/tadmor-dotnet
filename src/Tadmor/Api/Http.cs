using Tadmor.Services;

namespace Tadmor.Api;

/// <summary>Request helpers shared by the endpoint groups.</summary>
internal static class Http
{
    /// <summary>The request body as an Input; an absent body is an empty object.</summary>
    public static async Task<Input> Body(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        return Input.Parse(await reader.ReadToEndAsync(ctx.RequestAborted));
    }

    /// <summary>A path id: 400 unless a positive integer that fits.</summary>
    public static int Id(string text) =>
        text.Length > 0 && text.All(char.IsAsciiDigit) && text[0] != '0' && int.TryParse(text, out var id)
            ? id
            : throw ServiceException.BadRequest($"invalid id: {text}");

    /// <summary>An optional date query parameter: 400 unless YYYY-MM-DD.</summary>
    public static DateOnly? Date(HttpContext ctx, string name)
    {
        var s = ctx.Request.Query[name].ToString().Trim();
        if (s.Length == 0)
        {
            return null;
        }
        return Input.TryParseDate(s) ?? throw ServiceException.BadRequest($"{name} is not a date: {s}");
    }

    public static IResult Ok(object value) => Results.Json(value, Json.Options);

    public static IResult Created(object key) => Results.Json(key, Json.Options, statusCode: 201);

    public static IResult Error(int status, string message) =>
        Results.Json(new { error = message }, Json.Options, statusCode: status);
}
