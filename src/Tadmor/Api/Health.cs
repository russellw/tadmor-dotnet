using Npgsql;

namespace Tadmor.Api;

/// <summary>Liveness and readiness (spec/api.md §2).</summary>
internal static class Health
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));
        app.MapGet("/readyz", async (NpgsqlDataSource db, CancellationToken ct) =>
        {
            try
            {
                await using var cmd = db.CreateCommand("SELECT 1");
                await cmd.ExecuteScalarAsync(ct);
                return Results.Json(new { status = "ready" });
            }
            catch (Exception e) when (e is NpgsqlException or TimeoutException)
            {
                return Results.Json(new { status = "database unavailable" }, statusCode: 503);
            }
        });
    }
}
