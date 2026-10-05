using Tadmor.Ui;
using StockService = Tadmor.Services.Stock;

namespace Tadmor.Pages.Stock;

/// <summary>Stock movements, newest first (spec/domain.md §13 S1). Costs are in the base currency.</summary>
public sealed class ListModel(StockService stock, Lookups lookups) : UiPage
{
    public List<StockService.MovementDto> Rows { get; private set; } = [];
    public Dictionary<int, string> Products { get; private set; } = [];
    public Dictionary<int, string> Warehouses { get; private set; } = [];
    public string Base { get; private set; } = "";

    public async Task OnGetAsync()
    {
        Rows = await stock.ListAsync();
        Products = await lookups.ProductNamesAsync();
        Warehouses = await lookups.WarehouseNamesAsync();
        Base = await lookups.BaseCurrencyAsync();
    }
}
