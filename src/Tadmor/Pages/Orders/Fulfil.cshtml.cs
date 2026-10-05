using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Orders;

/// <summary>
/// Invoicing or billing an open order (spec/domain.md §13 O5), and shipping
/// or receiving it (O6): each outstanding line offered with its remaining
/// quantity, which may be lowered for a partial fulfilment.
/// </summary>
public sealed class FulfilModel(Lookups lookups) : UiPage
{
    public OrderKind Kind { get; private set; } = null!;
    public int Id { get; private set; }
    public bool Charge { get; private set; }
    public string Verb { get; private set; } = "";
    public Dictionary<string, object?> Order { get; private set; } = [];
    public List<Dictionary<string, object?>> Outstanding { get; private set; } = [];
    public List<Option> Warehouses { get; private set; } = [];
    public List<int> Movements { get; private set; } = [];

    private IOrders Orders(string kind) => HttpContext.RequestServices.GetRequiredKeyedService<IOrders>(kind);

    private async Task<IActionResult> LoadAsync(string kind, int id, string step)
    {
        var orders = Orders(kind);
        Kind = orders.Kind;
        Id = id;
        Charge = step is "invoice" or "bill";
        if (step != (Charge ? (Kind.Sales ? "invoice" : "bill") : (Kind.Sales ? "ship" : "receive")))
        {
            throw ServiceException.NotFound();
        }
        Verb = char.ToUpperInvariant(step[0]) + step[1..];
        Order = await orders.GetAsync(id);
        var products = (await lookups.ProductsAsync()).Where(p => p.TrackInventory && p.IsActive).Select(p => p.Id).ToHashSet();
        // Shipping and receiving offer only the stocked lines.
        Outstanding = (await orders.LinesAsync(id))
            .Where(l => Format.Dec(l[Charge ? Kind.ToCharge : Kind.ToMove]) > 0 && (Charge || l["product_id"] is int p && products.Contains(p)))
            .ToList();
        Warehouses = await lookups.WarehouseOptionsAsync();
        return Page();
    }

    public Task<IActionResult> OnGetAsync(string kind, int id, string step) => LoadAsync(kind, id, step);

    public async Task<IActionResult> OnPostAsync(string kind, int id, string step)
    {
        await LoadAsync(kind, id, step);
        return await Attempt(async () =>
        {
            if (Charge)
            {
                var doc = await Orders(kind).ChargeAsync(id, Form());
                Notice = $"Created a draft {(Kind.Sales ? "invoice" : "bill")} from the order.";
                return Redirect($"/{(Kind.Sales ? "sales-invoices" : "purchase-bills")}/{doc}");
            }
            Movements = await Orders(kind).MoveAsync(id, Form());
            return Page();
        }, () => LoadAsync(kind, id, step));
    }
}
