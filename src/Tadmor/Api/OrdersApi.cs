using Tadmor.Db;
using Tadmor.Services;

namespace Tadmor.Api;

/// <summary>Orders and their fulfilment, and stock movements (spec/api.md §5.10, §5.12).</summary>
internal static class OrdersApi
{
    public static void Map(IEndpointRouteBuilder api)
    {
        MapOrders<SalesOrder, SalesOrderLine, SalesOrderLineFulfilment, SalesOrderFulfilment, SalesInvoice, SalesInvoiceLine>(
            api, OrderKind.SalesOrder, "invoice", "ship");
        MapOrders<PurchaseOrder, PurchaseOrderLine, PurchaseOrderLineFulfilment, PurchaseOrderFulfilment, PurchaseBill, PurchaseBillLine>(
            api, OrderKind.PurchaseOrder, "bill", "receive");

        var g = api.MapGroup("/stock-movements");
        g.MapGet("", async (Stock s) => Http.Ok(await s.ListAsync()));
        g.MapGet("/{id}", async (string id, Stock s) => Http.Ok(await s.GetAsync(Http.Id(id))));
        g.MapPost("", async (HttpContext ctx, Stock s) => Http.Created(new { id = await s.CreateAsync(await Http.Body(ctx)) }));
        g.MapPut("/{id}", async (string id, HttpContext ctx, Stock s) =>
        {
            var key = Http.Id(id);
            await s.UpdateAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });
        g.MapDelete("/{id}", async (string id, Stock s) =>
        {
            await s.DeleteAsync(Http.Id(id));
            return Results.NoContent();
        });
        g.MapPost("/{id}/post", async (string id, HttpContext ctx, Stock s) =>
        {
            var key = Http.Id(id);
            return Http.Ok(new { journal_entry_id = await s.PostAsync(key, await Http.Body(ctx)) });
        });
        g.MapPost("/{id}/unpost", async (string id, HttpContext ctx, Stock s) =>
        {
            ctx.RequireAdmin();
            return Http.Ok(new { reversal_entry_id = await s.UnpostAsync(Http.Id(id)) });
        });
        api.MapGet("/inventory-valuation", async (Stock s) => Http.Ok(await s.ValuationAsync()));
    }

    private static void MapOrders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine>(IEndpointRouteBuilder api, OrderKind kind,
        string charge, string move)
        where TOrder : Order, new() where TLine : DocumentLine, new() where TLineFul : OrderLineFulfilment
        where TFul : OrderFulfilment where TDoc : LineDocument, new() where TDocLine : DocumentLine, new()
    {
        var g = api.MapGroup("/" + kind.Collection);
        g.MapGet("", async (Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) => Http.Ok(await s.ListAsync()));
        g.MapGet("/{id}", async (string id, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
            Http.Ok(await s.GetAsync(Http.Id(id))));
        g.MapGet("/{id}/lines", async (string id, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
            Http.Ok(await s.LinesAsync(Http.Id(id))));
        g.MapPost("", async (HttpContext ctx, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
            Http.Created(new { id = await s.CreateAsync(await Http.Body(ctx)) }));
        g.MapPut("/{id}", async (string id, HttpContext ctx, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
        {
            var key = Http.Id(id);
            await s.UpdateAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });
        g.MapDelete("/{id}", async (string id, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
        {
            await s.DeleteAsync(Http.Id(id));
            return Results.NoContent();
        });
        g.MapPost("/{id}/confirm", async (string id, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
        {
            await s.ConfirmAsync(Http.Id(id));
            return Results.NoContent();
        });
        g.MapPost("/{id}/close", async (string id, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
        {
            await s.CloseAsync(Http.Id(id));
            return Results.NoContent();
        });
        g.MapPost("/{id}/cancel", async (string id, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
        {
            await s.CancelAsync(Http.Id(id));
            return Results.NoContent();
        });
        g.MapPost("/{id}/" + charge, async (string id, HttpContext ctx, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
        {
            var key = Http.Id(id);
            var doc = await s.ChargeAsync(key, await Http.Body(ctx));
            return Http.Created(new Dictionary<string, object> { [kind.ChargeKey] = doc });
        });
        g.MapPost("/{id}/" + move, async (string id, HttpContext ctx, Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine> s) =>
        {
            var key = Http.Id(id);
            return Http.Created(new { movement_ids = await s.MoveAsync(key, await Http.Body(ctx)) });
        });
    }

    public static void AddServices(IServiceCollection services)
    {
        services.AddScoped<Stock>();
        services.AddScoped(sp => new Orders<SalesOrder, SalesOrderLine, SalesOrderLineFulfilment, SalesOrderFulfilment, SalesInvoice,
            SalesInvoiceLine>(sp.GetRequiredService<TadmorDb>(), OrderKind.SalesOrder));
        services.AddScoped(sp => new Orders<PurchaseOrder, PurchaseOrderLine, PurchaseOrderLineFulfilment, PurchaseOrderFulfilment,
            PurchaseBill, PurchaseBillLine>(sp.GetRequiredService<TadmorDb>(), OrderKind.PurchaseOrder));
    }
}
