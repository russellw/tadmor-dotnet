using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Payments;

/// <summary>A payment list, newest first (spec/domain.md §13 P1).</summary>
public sealed class ListModel(Lookups lookups) : UiPage
{
    public PayKind Kind { get; private set; } = null!;
    public List<Dictionary<string, object?>> Rows { get; private set; } = [];
    private Dictionary<int, string> parties = [];

    public async Task OnGetAsync(string kind)
    {
        var payments = HttpContext.RequestServices.GetRequiredKeyedService<IPayments>(kind);
        Kind = payments.Kind;
        Rows = await payments.ListAsync();
        parties = await lookups.PartyNamesAsync(Kind.Sales);
    }

    public string PartyName(object? id) => id is int i ? parties.GetValueOrDefault(i, "") : "";
}
