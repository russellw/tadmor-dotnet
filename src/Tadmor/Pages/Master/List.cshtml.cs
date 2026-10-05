using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Master;

/// <summary>A master-data list, in the API's order, each row opening its form (spec/domain.md §13 M1 to M7).</summary>
public sealed class ListModel(Lookups lookups) : UiPage
{
    public Resource Resource { get; private set; } = null!;
    public List<Dictionary<string, object?>> Rows { get; private set; } = [];
    private Dictionary<int, string> organizations = [];

    public async Task OnGetAsync(string resource)
    {
        Resource = Resource.Find(resource) ?? throw ServiceException.NotFound();
        if (Resource.AdminOnly)
        {
            RequireAdmin();
        }
        Rows = Resource.Rows(await Resource.List(HttpContext.RequestServices)).ToList();
        organizations = await lookups.OrganizationNamesAsync();
    }

    public string Cell(Column c, object? v) => c.Cell switch
    {
        Ui.Cell.Amount => Format.Amount(Format.Dec(v)),
        Ui.Cell.Qty => Format.Qty(Format.Dec(v)),
        Ui.Cell.Flag => Format.Raw(v) == "true" ? "Yes" : "",
        Ui.Cell.Active => Format.Raw(v) == "true" ? "Active" : "Inactive",
        Ui.Cell.Role => Format.Raw(v) == "true" ? "Administrator" : "User",
        Ui.Cell.Organization => Format.Int(v) is { } id ? organizations.GetValueOrDefault(id, "") : "",
        _ => Format.Raw(v),
    };
}
