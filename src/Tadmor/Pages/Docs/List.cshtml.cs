using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Docs;

/// <summary>An invoice, bill, or credit-note list, newest first (spec/domain.md §13 D1).</summary>
public sealed class ListModel(Lookups lookups) : UiPage
{
    public DocKind Kind { get; private set; } = null!;
    public List<Dictionary<string, object?>> Rows { get; private set; } = [];
    private Dictionary<int, string> parties = [];

    public async Task OnGetAsync(string kind)
    {
        var docs = HttpContext.RequestServices.GetRequiredKeyedService<IDocuments>(kind);
        Kind = docs.Kind;
        Rows = await docs.ListAsync();
        parties = await lookups.PartyNamesAsync(Kind.Sales);
    }

    public string PartyName(object? id) => id is int i ? parties.GetValueOrDefault(i, "") : "";
}
