using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Orders;

/// <summary>An order list, newest first, with both fulfilment statuses (spec/domain.md §13 O1).</summary>
public sealed class ListModel(Lookups lookups) : UiPage
{
    public OrderKind Kind { get; private set; } = null!;
    public List<Dictionary<string, object?>> Rows { get; private set; } = [];
    private Dictionary<int, string> parties = [];

    public async Task OnGetAsync(string kind)
    {
        var orders = HttpContext.RequestServices.GetRequiredKeyedService<IOrders>(kind);
        Kind = orders.Kind;
        Rows = await orders.ListAsync();
        parties = await lookups.PartyNamesAsync(Kind.Sales);
    }

    public string PartyName(object? id) => id is int i ? parties.GetValueOrDefault(i, "") : "";
}
