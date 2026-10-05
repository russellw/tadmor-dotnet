using Microsoft.AspNetCore.Mvc;
using Tadmor.Ui;
using StockService = Tadmor.Services.Stock;

namespace Tadmor.Pages.Stock;

/// <summary>A stock movement and its actions (spec/domain.md §13 S3).</summary>
public sealed class DetailModel(StockService stock, Lookups lookups) : UiPage
{
    public StockService.MovementDto Movement { get; private set; } = null!;
    public string Product { get; private set; } = "";
    public string Warehouse { get; private set; } = "";
    public string Base { get; private set; } = "";
    public List<Option> CreditAccounts { get; private set; } = [];
    /// <summary>The seeded Goods Received Not Invoiced account, proposed for receipts.</summary>
    public string? Grni { get; private set; }

    private async Task<IActionResult> LoadAsync(int id)
    {
        Movement = await stock.GetAsync(id);
        Product = (await lookups.ProductNamesAsync()).GetValueOrDefault(Movement.ProductId, "");
        Warehouse = (await lookups.WarehouseNamesAsync()).GetValueOrDefault(Movement.WarehouseId, "");
        Base = await lookups.BaseCurrencyAsync();
        CreditAccounts = await lookups.PostableAccountsAsync();
        Grni = (await lookups.AccountsAsync()).FirstOrDefault(a => a.Code == "2150")?.Id.ToString();
        return Page();
    }

    public Task<IActionResult> OnGetAsync(int id) => LoadAsync(id);

    public Task<IActionResult> OnPostPostAsync(int id) =>
        Attempt(async () =>
        {
            Notice = $"Posted as journal entry {await stock.PostAsync(id, Form())}.";
            return Redirect($"/stock-movements/{id}");
        }, () => LoadAsync(id));

    public Task<IActionResult> OnPostUnpostAsync(int id)
    {
        RequireAdmin();
        return Attempt(async () =>
        {
            Notice = $"Unposted; reversal entry {await stock.UnpostAsync(id)}.";
            return Redirect($"/stock-movements/{id}");
        }, () => LoadAsync(id));
    }

    public Task<IActionResult> OnPostDeleteAsync(int id) =>
        Attempt(async () =>
        {
            await stock.DeleteAsync(id);
            Notice = "Deleted.";
            return Redirect("/stock-movements");
        }, () => LoadAsync(id));
}
