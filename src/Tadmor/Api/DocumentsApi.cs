using Tadmor.Db;
using Tadmor.Services;

namespace Tadmor.Api;

/// <summary>Invoices, bills, credit notes, payments, and their applications (spec/api.md §5.9).</summary>
internal static class DocumentsApi
{
    public static void Map(IEndpointRouteBuilder api)
    {
        MapDocuments<SalesInvoice, SalesInvoiceLine, SalesInvoiceBalance>(api, DocKind.SalesInvoice, null);
        MapDocuments<PurchaseBill, PurchaseBillLine, PurchaseBillBalance>(api, DocKind.PurchaseBill, null);
        MapDocuments<SalesCreditNote, SalesCreditNoteLine, SalesCreditNoteBalance>(api, DocKind.SalesCreditNote, SettlerKind.SalesCreditNote);
        MapDocuments<PurchaseCreditNote, PurchaseCreditNoteLine, PurchaseCreditNoteBalance>(api, DocKind.PurchaseCreditNote, SettlerKind.PurchaseCreditNote);
        MapPayments<CustomerPayment>(api, PayKind.Customer);
        MapPayments<SupplierPayment>(api, PayKind.Supplier);

        api.MapGet("/journal-entries/{id}", async (string id, Reports s) => Http.Ok(await s.EntryAsync(Http.Id(id))));
        api.MapGet("/accounts/{id}/ledger", async (string id, HttpContext ctx, Reports s) =>
        {
            var key = Http.Id(id);
            return Http.Ok(await s.LedgerAsync(key, Http.Date(ctx, "from"), Http.Date(ctx, "to")));
        });
        api.MapGet("/trial-balance", async (Reports s) => Http.Ok(await s.TrialBalanceAsync()));
    }

    private static void MapDocuments<TDoc, TLine, TBal>(IEndpointRouteBuilder api, DocKind kind, SettlerKind? settler)
        where TDoc : LineDocument where TLine : DocumentLine, new() where TBal : DocumentBalance
    {
        var g = api.MapGroup("/" + kind.Collection);
        g.MapGet("", async (Documents<TDoc, TLine, TBal> s) => Http.Ok(await s.ListAsync()));
        g.MapGet("/{id}", async (string id, Documents<TDoc, TLine, TBal> s) => Http.Ok(await s.GetAsync(Http.Id(id))));
        g.MapGet("/{id}/lines", async (string id, Documents<TDoc, TLine, TBal> s) => Http.Ok(await s.LinesAsync(Http.Id(id))));
        g.MapPost("", async (HttpContext ctx, Documents<TDoc, TLine, TBal> s) =>
            Http.Created(new { id = await s.CreateAsync(await Http.Body(ctx)) }));
        g.MapPut("/{id}", async (string id, HttpContext ctx, Documents<TDoc, TLine, TBal> s) =>
        {
            var key = Http.Id(id);
            await s.UpdateAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });
        g.MapDelete("/{id}", async (string id, Documents<TDoc, TLine, TBal> s) =>
        {
            await s.DeleteAsync(Http.Id(id));
            return Results.NoContent();
        });
        g.MapPost("/{id}/post", async (string id, Documents<TDoc, TLine, TBal> s) =>
            Http.Ok(new { journal_entry_id = await s.PostAsync(Http.Id(id)) }));
        g.MapPost("/{id}/unpost", async (string id, HttpContext ctx, Documents<TDoc, TLine, TBal> s) =>
        {
            ctx.RequireAdmin();
            return Http.Ok(new { reversal_entry_id = await s.UnpostAsync(Http.Id(id)) });
        });
        if (settler is not null)
        {
            MapApplications(g, settler);
        }
    }

    private static void MapPayments<TPay>(IEndpointRouteBuilder api, PayKind kind) where TPay : Payment, new()
    {
        var g = api.MapGroup("/" + kind.Collection);
        g.MapGet("", async (Payments<TPay> s) => Http.Ok(await s.ListAsync()));
        g.MapGet("/{id}", async (string id, Payments<TPay> s) => Http.Ok(await s.GetAsync(Http.Id(id))));
        g.MapPost("", async (HttpContext ctx, Payments<TPay> s) =>
            Http.Created(new { id = await s.CreateAsync(await Http.Body(ctx)) }));
        g.MapPut("/{id}", async (string id, HttpContext ctx, Payments<TPay> s) =>
        {
            var key = Http.Id(id);
            await s.UpdateAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });
        g.MapDelete("/{id}", async (string id, Payments<TPay> s) =>
        {
            await s.DeleteAsync(Http.Id(id));
            return Results.NoContent();
        });
        g.MapPost("/{id}/post", async (string id, Payments<TPay> s) =>
            Http.Ok(new { journal_entry_id = await s.PostAsync(Http.Id(id)) }));
        g.MapPost("/{id}/unpost", async (string id, HttpContext ctx, Payments<TPay> s) =>
        {
            ctx.RequireAdmin();
            return Http.Ok(new { reversal_entry_id = await s.UnpostAsync(Http.Id(id)) });
        });
        MapApplications(g, kind.Settler);
    }

    private static void MapApplications(RouteGroupBuilder g, SettlerKind settler)
    {
        g.MapGet("/{id}/applications", async (string id, Settlement s) => Http.Ok(await s.ApplicationsAsync(settler, Http.Id(id))));
        g.MapPost("/{id}/apply", async (string id, Settlement s) =>
        {
            var created = await s.ApplyAsync(settler, Http.Id(id));
            return Http.Ok(new { applications = created });
        });
    }

    public static void AddServices(IServiceCollection services)
    {
        services.AddScoped<Journal>();
        services.AddScoped<Settlement>();
        services.AddScoped<Reports>();
        AddDocuments<SalesInvoice, SalesInvoiceLine, SalesInvoiceBalance>(services, DocKind.SalesInvoice);
        AddDocuments<PurchaseBill, PurchaseBillLine, PurchaseBillBalance>(services, DocKind.PurchaseBill);
        AddDocuments<SalesCreditNote, SalesCreditNoteLine, SalesCreditNoteBalance>(services, DocKind.SalesCreditNote);
        AddDocuments<PurchaseCreditNote, PurchaseCreditNoteLine, PurchaseCreditNoteBalance>(services, DocKind.PurchaseCreditNote);
        services.AddScoped(sp => new Payments<CustomerPayment>(sp.GetRequiredService<TadmorDb>(), sp.GetRequiredService<Journal>(),
            sp.GetRequiredService<Settlement>(), PayKind.Customer));
        services.AddScoped(sp => new Payments<SupplierPayment>(sp.GetRequiredService<TadmorDb>(), sp.GetRequiredService<Journal>(),
            sp.GetRequiredService<Settlement>(), PayKind.Supplier));
    }

    private static void AddDocuments<TDoc, TLine, TBal>(IServiceCollection services, DocKind kind)
        where TDoc : LineDocument where TLine : DocumentLine, new() where TBal : DocumentBalance =>
        services.AddScoped(sp => new Documents<TDoc, TLine, TBal>(sp.GetRequiredService<TadmorDb>(), sp.GetRequiredService<Journal>(), kind));
}
