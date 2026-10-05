using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Tadmor.Services;
using Tadmor.Ui;
using StockService = Tadmor.Services.Stock;

namespace Tadmor.Pages.Stock;

/// <summary>
/// The stock movement form (spec/domain.md §13 S2): the quantity is entered
/// as a magnitude and signed by the type; an adjustment keeps the sign typed.
/// </summary>
public sealed class EditModel(StockService stock, Lookups lookups) : UiPage
{
    public int? Id { get; private set; }
    public Dictionary<string, string> Values { get; } = [];
    public List<Option> Products { get; private set; } = [];
    public List<Option> Warehouses { get; private set; } = [];
    public string Base { get; private set; } = "";

    private async Task LoadAsync(int? id, bool fromPost)
    {
        Id = id;
        if (fromPost)
        {
            foreach (var (k, v) in Request.Form)
            {
                Values[k] = v.ToString();
            }
        }
        else if (id is { } i)
        {
            var m = await stock.GetAsync(i);
            Values["product_id"] = m.ProductId.ToString();
            Values["warehouse_id"] = m.WarehouseId.ToString();
            Values["movement_type"] = m.MovementType;
            Values["movement_date"] = Format.Date(m.MovementDate);
            Values["quantity"] = Format.Qty(m.MovementType == "adjustment" ? m.Quantity : Math.Abs(m.Quantity));
            Values["unit_cost"] = Format.Qty(m.UnitCost);
            Values["reference"] = m.Reference ?? "";
            Values["notes"] = m.Notes ?? "";
        }
        else
        {
            Values["movement_date"] = Format.Date(StockService.Today);
        }
        // Only tracked, active products may move (spec/domain.md §3).
        Products = (await lookups.ProductsAsync()).Where(p => p.TrackInventory && (p.IsActive || p.Id.ToString() == Values.GetValueOrDefault("product_id")))
            .Select(p => new Option(p.Id.ToString(), $"{p.Sku} {p.Name}")).ToList();
        Warehouses = await lookups.WarehouseOptionsAsync();
        Base = await lookups.BaseCurrencyAsync();
    }

    /// <summary>The posted form with the quantity signed by the movement type.</summary>
    private Input Signed()
    {
        var fields = Request.Form.ToDictionary(kv => kv.Key, kv => kv.Value);
        var qty = fields.GetValueOrDefault("quantity").ToString().Trim();
        var type = fields.GetValueOrDefault("movement_type").ToString();
        if (qty.Length > 0 && type != "adjustment")
        {
            var magnitude = qty.TrimStart('+', '-');
            fields["quantity"] = new StringValues(type is "issue" or "transfer_out" ? "-" + magnitude : magnitude);
        }
        return FormInput.From(new FormCollection(fields));
    }

    public Task OnGetAsync(int? id) => LoadAsync(id, fromPost: false);

    public async Task<IActionResult> OnPostAsync(int? id) =>
        await Attempt(async () =>
        {
            var saved = id ?? 0;
            if (id is { } i)
            {
                await stock.UpdateAsync(i, Signed());
            }
            else
            {
                saved = await stock.CreateAsync(Signed());
            }
            Notice = "Saved.";
            return Redirect($"/stock-movements/{saved}");
        }, async () =>
        {
            await LoadAsync(id, fromPost: true);
            return Page();
        });
}
