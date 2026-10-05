using Tadmor.Services;

namespace Tadmor.Api;

/// <summary>Master data, the fiscal calendar, settings, and exchange rates (spec/api.md §5.2 to §5.8).</summary>
internal static class MasterApi
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/organizations", async (Organizations s) => Http.Ok(await s.ListAsync()));
        api.MapGet("/organizations/{id}", async (string id, Organizations s) => Http.Ok(await s.GetAsync(Http.Id(id))));
        api.MapPost("/organizations", async (HttpContext ctx, Organizations s) =>
            Http.Created(new { id = await s.CreateAsync(await Http.Body(ctx)) }));
        api.MapPut("/organizations/{id}", async (string id, HttpContext ctx, Organizations s) =>
        {
            var key = Http.Id(id);
            await s.UpdateAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });

        api.MapGet("/customers", async (Parties s) => Http.Ok(await s.CustomersAsync()));
        api.MapGet("/customers/{id}", async (string id, Parties s) => Http.Ok(await s.CustomerAsync(Http.Id(id))));
        api.MapPost("/customers", async (HttpContext ctx, Parties s) =>
            Http.Created(new { id = await s.CreateCustomerAsync(await Http.Body(ctx)) }));
        api.MapPut("/customers/{id}", async (string id, HttpContext ctx, Parties s) =>
        {
            var key = Http.Id(id);
            await s.UpdateCustomerAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });
        api.MapGet("/suppliers", async (Parties s) => Http.Ok(await s.SuppliersAsync()));
        api.MapGet("/suppliers/{id}", async (string id, Parties s) => Http.Ok(await s.SupplierAsync(Http.Id(id))));
        api.MapPost("/suppliers", async (HttpContext ctx, Parties s) =>
            Http.Created(new { id = await s.CreateSupplierAsync(await Http.Body(ctx)) }));
        api.MapPut("/suppliers/{id}", async (string id, HttpContext ctx, Parties s) =>
        {
            var key = Http.Id(id);
            await s.UpdateSupplierAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });

        api.MapGet("/products", async (Products s) => Http.Ok(await s.ListAsync()));
        api.MapGet("/products/{id}", async (string id, Products s) => Http.Ok(await s.GetAsync(Http.Id(id))));
        api.MapPost("/products", async (HttpContext ctx, Products s) =>
            Http.Created(new { id = await s.CreateAsync(await Http.Body(ctx)) }));
        api.MapPut("/products/{id}", async (string id, HttpContext ctx, Products s) =>
        {
            var key = Http.Id(id);
            await s.UpdateAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });

        api.MapGet("/accounts", async (Accounts s) => Http.Ok(await s.ListAsync()));
        api.MapGet("/accounts/{id}", async (string id, Accounts s) => Http.Ok(await s.GetAsync(Http.Id(id))));
        api.MapPost("/accounts", async (HttpContext ctx, Accounts s) =>
            Http.Created(new { id = await s.CreateAsync(await Http.Body(ctx)) }));
        api.MapPut("/accounts/{id}", async (string id, HttpContext ctx, Accounts s) =>
        {
            var key = Http.Id(id);
            await s.UpdateAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });

        api.MapGet("/tax-codes", async (Catalog s) => Http.Ok(await s.TaxCodesAsync()));
        api.MapGet("/tax-codes/{code}", async (string code, Catalog s) => Http.Ok(await s.TaxCodeAsync(code)));
        api.MapPost("/tax-codes", async (HttpContext ctx, Catalog s) =>
            Http.Created(new { code = await s.CreateTaxCodeAsync(await Http.Body(ctx)) }));
        api.MapPut("/tax-codes/{code}", async (string code, HttpContext ctx, Catalog s) =>
        {
            await s.UpdateTaxCodeAsync(code, await Http.Body(ctx));
            return Results.NoContent();
        });
        api.MapGet("/payment-terms", async (Catalog s) => Http.Ok(await s.PaymentTermsAsync()));
        api.MapGet("/payment-terms/{code}", async (string code, Catalog s) => Http.Ok(await s.PaymentTermAsync(code)));
        api.MapPost("/payment-terms", async (HttpContext ctx, Catalog s) =>
            Http.Created(new { code = await s.CreatePaymentTermAsync(await Http.Body(ctx)) }));
        api.MapPut("/payment-terms/{code}", async (string code, HttpContext ctx, Catalog s) =>
        {
            await s.UpdatePaymentTermAsync(code, await Http.Body(ctx));
            return Results.NoContent();
        });
        api.MapGet("/warehouses", async (Catalog s) => Http.Ok(await s.WarehousesAsync()));
        api.MapGet("/warehouses/{id}", async (string id, Catalog s) => Http.Ok(await s.WarehouseAsync(Http.Id(id))));
        api.MapPost("/warehouses", async (HttpContext ctx, Catalog s) =>
            Http.Created(new { id = await s.CreateWarehouseAsync(await Http.Body(ctx)) }));
        api.MapPut("/warehouses/{id}", async (string id, HttpContext ctx, Catalog s) =>
        {
            var key = Http.Id(id);
            await s.UpdateWarehouseAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });

        api.MapGet("/fiscal-years", async (Calendar s) => Http.Ok(await s.FiscalYearsAsync()));
        api.MapGet("/fiscal-years/{id}", async (string id, Calendar s) => Http.Ok(await s.FiscalYearAsync(Http.Id(id))));
        api.MapPost("/fiscal-years", async (HttpContext ctx, Calendar s) =>
            Http.Created(new { id = await s.CreateFiscalYearAsync(await Http.Body(ctx)) }));
        api.MapPut("/fiscal-years/{id}", async (string id, HttpContext ctx, Calendar s) =>
        {
            var key = Http.Id(id);
            await s.UpdateFiscalYearAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });
        api.MapGet("/accounting-periods", async (Calendar s) => Http.Ok(await s.PeriodsAsync()));
        api.MapGet("/accounting-periods/{id}", async (string id, Calendar s) => Http.Ok(await s.PeriodAsync(Http.Id(id))));
        api.MapPost("/accounting-periods", async (HttpContext ctx, Calendar s) =>
            Http.Created(new { id = await s.CreatePeriodAsync(await Http.Body(ctx)) }));
        api.MapPut("/accounting-periods/{id}", async (string id, HttpContext ctx, Calendar s) =>
        {
            var key = Http.Id(id);
            await s.UpdatePeriodAsync(key, await Http.Body(ctx));
            return Results.NoContent();
        });

        api.MapGet("/settings", async (Ledger s) => Http.Ok(await s.SettingsAsync()));
        api.MapPut("/settings", async (HttpContext ctx, Ledger s) =>
        {
            ctx.RequireAdmin();
            await s.UpdateSettingsAsync(await Http.Body(ctx));
            return Results.NoContent();
        });
        api.MapGet("/exchange-rates", async (Ledger s) => Http.Ok(await s.RatesAsync()));
        api.MapPost("/exchange-rates", async (HttpContext ctx, Ledger s) =>
        {
            var (currency, date) = await s.CreateRateAsync(await Http.Body(ctx));
            return Http.Created(new { currency_code = currency, rate_date = date });
        });
        api.MapPut("/exchange-rates/{currency}/{date}", async (string currency, string date, HttpContext ctx, Ledger s) =>
        {
            await s.UpdateRateAsync(currency, PathDate(date), await Http.Body(ctx));
            return Results.NoContent();
        });
        api.MapDelete("/exchange-rates/{currency}/{date}", async (string currency, string date, Ledger s) =>
        {
            await s.DeleteRateAsync(currency, PathDate(date));
            return Results.NoContent();
        });
    }

    private static DateOnly PathDate(string s) =>
        Input.TryParseDate(s) ?? throw ServiceException.BadRequest($"invalid date: {s}");
}
