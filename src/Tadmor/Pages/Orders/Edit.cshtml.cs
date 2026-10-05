using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Orders;

/// <summary>The order form, drafts only (spec/domain.md §13 O2).</summary>
public sealed class EditModel(Lookups lookups) : UiPage
{
    public OrderKind Kind { get; private set; } = null!;
    public int? Id { get; private set; }
    public LineForm Doc { get; private set; } = null!;

    private async Task LoadAsync(string kind, int? id, bool fromPost)
    {
        var orders = HttpContext.RequestServices.GetRequiredKeyedService<IOrders>(kind);
        Kind = orders.Kind;
        Id = id;
        Doc = new LineForm { Sales = Kind.Sales, PriceField = Kind.PriceField, AccountField = Kind.AccountField };
        if (fromPost)
        {
            Doc.FromPost(Request.Form);
        }
        else if (id is { } i)
        {
            Doc.FromRecord(await orders.GetAsync(i), await orders.LinesAsync(i));
        }
        else
        {
            Doc.Header["order_date"] = Format.Date(Format.Today);
            Doc.Header["currency_code"] = await lookups.BaseCurrencyAsync();
        }
        await Doc.LoadChoicesAsync(lookups);
    }

    public Task OnGetAsync(string kind, int? id) => LoadAsync(kind, id, fromPost: false);

    public async Task<IActionResult> OnPostAsync(string kind, int? id)
    {
        var orders = HttpContext.RequestServices.GetRequiredKeyedService<IOrders>(kind);
        return await Attempt(async () =>
        {
            var saved = id ?? 0;
            if (id is { } i)
            {
                await orders.UpdateAsync(i, Form());
            }
            else
            {
                saved = await orders.CreateAsync(Form());
            }
            Notice = "Saved.";
            return Redirect($"/{kind}/{saved}");
        }, async () =>
        {
            await LoadAsync(kind, id, fromPost: true);
            return Page();
        });
    }
}
