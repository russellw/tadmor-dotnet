using Tadmor.Services;

namespace Tadmor.Api;

/// <summary>Year-end, banking, reports, printing, and email (spec/api.md §5.7, §5.11, §5.13, §5.14).</summary>
internal static class LedgerApi
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/fiscal-years/{id}/close", async (string id, HttpContext ctx, YearEnd s) =>
        {
            ctx.RequireAdmin();
            var key = Http.Id(id);
            var (closing, next) = await s.CloseAsync(key, await Http.Body(ctx));
            return Http.Ok(new { closing_entry_id = closing, next_fiscal_year_id = next });
        });
        api.MapPost("/fiscal-years/{id}/reopen", async (string id, HttpContext ctx, YearEnd s) =>
        {
            ctx.RequireAdmin();
            return Http.Ok(new { reversal_entry_id = await s.ReopenAsync(Http.Id(id)) });
        });

        var b = api.MapGroup("/bank-statements");
        b.MapGet("", async (Banking s) => Http.Ok(await s.ListAsync()));
        b.MapGet("/{id}", async (string id, Banking s) => Http.Ok(await s.GetAsync(Http.Id(id))));
        b.MapPost("", async (HttpContext ctx, Banking s) => Http.Created(new { id = await s.CreateAsync(await Http.Body(ctx)) }));
        b.MapPut("/{id}", async (string id, HttpContext ctx, Banking s) =>
        {
            var key = Http.Id(id);
            await s.UpdateAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });
        b.MapDelete("/{id}", async (string id, Banking s) =>
        {
            await s.DeleteAsync(Http.Id(id));
            return Results.NoContent();
        });
        b.MapGet("/{id}/lines", async (string id, Banking s) => Http.Ok(await s.LinesAsync(Http.Id(id))));
        b.MapPost("/{id}/lines", async (string id, HttpContext ctx, Banking s) =>
        {
            var key = Http.Id(id);
            return Http.Created(new { id = await s.AddLineAsync(key, await Http.Body(ctx)) });
        });
        b.MapPost("/{id}/import", async (string id, HttpContext ctx, Banking s) =>
        {
            var key = Http.Id(id);
            return Http.Ok(new { imported = await s.ImportAsync(key, await Http.Body(ctx)) });
        });
        b.MapGet("/{id}/candidates", async (string id, Banking s) => Http.Ok(await s.CandidatesAsync(Http.Id(id))));
        b.MapPost("/{id}/auto-match", async (string id, Banking s) => Http.Ok(new { matched = await s.AutoMatchAsync(Http.Id(id)) }));
        b.MapPost("/{id}/reconcile", async (string id, Banking s) =>
        {
            await s.ReconcileAsync(Http.Id(id));
            return Results.NoContent();
        });
        b.MapPost("/{id}/reopen", async (string id, HttpContext ctx, Banking s) =>
        {
            ctx.RequireAdmin();
            await s.ReopenAsync(Http.Id(id));
            return Results.NoContent();
        });
        api.MapPost("/bank-statement-lines/{id}/match", async (string id, HttpContext ctx, Banking s) =>
        {
            var key = Http.Id(id);
            await s.MatchAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });
        api.MapPost("/bank-statement-lines/{id}/unmatch", async (string id, Banking s) =>
        {
            await s.UnmatchAsync(Http.Id(id));
            return Results.NoContent();
        });
        api.MapDelete("/bank-statement-lines/{id}", async (string id, Banking s) =>
        {
            await s.DeleteLineAsync(Http.Id(id));
            return Results.NoContent();
        });

        api.MapGet("/profit-and-loss", async (HttpContext ctx, Reports s) =>
            Http.Ok(await s.ProfitAndLossAsync(Http.Date(ctx, "from"), Http.Date(ctx, "to"))));
        api.MapGet("/balance-sheet", async (HttpContext ctx, Reports s) => Http.Ok(await s.BalanceSheetAsync(Http.Date(ctx, "as_of"))));
        api.MapGet("/cash-flow", async (HttpContext ctx, Reports s) =>
            Http.Ok(await s.CashFlowAsync(Http.Date(ctx, "from"), Http.Date(ctx, "to"))));
        api.MapGet("/ar-aging", async (Reports s) => Http.Ok(await s.AgingAsync(receivables: true)));
        api.MapGet("/ap-aging", async (Reports s) => Http.Ok(await s.AgingAsync(receivables: false)));

        foreach (var kind in PrintKind.All)
        {
            api.MapGet($"/{kind.Collection}/{{id}}/pdf", async (string id, HttpContext ctx, Printer s) =>
            {
                var pdf = await s.PdfAsync(kind, Http.Id(id));
                ctx.Response.Headers.ContentDisposition = $"inline; filename=\"{pdf.FileName}\"";
                return Results.Bytes(pdf.Bytes, "application/pdf");
            });
            api.MapPost($"/{kind.Collection}/{{id}}/email", async (string id, HttpContext ctx, Printer s) =>
            {
                var key = Http.Id(id);
                var to = await s.EmailAsync(kind, key, await Http.Body(ctx));
                return Http.Ok(new { status = "sent", to });
            });
        }
    }

    public static void AddServices(IServiceCollection services)
    {
        services.AddScoped<YearEnd>();
        services.AddScoped<Banking>();
        services.AddScoped<Printer>();
        services.AddSingleton<Mailer>();
    }
}
