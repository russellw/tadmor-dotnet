using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Docs;

/// <summary>
/// The invoice, bill, and credit-note form (spec/domain.md §13 D2): the
/// header and any number of lines, totals previewed as the user types.
/// Drafts only; the server refuses the rest, and says why.
/// </summary>
public sealed class EditModel(Lookups lookups) : UiPage
{
    public DocKind Kind { get; private set; } = null!;
    public int? Id { get; private set; }
    public LineForm Doc { get; private set; } = null!;

    private async Task LoadAsync(string kind, int? id, bool fromPost)
    {
        var docs = HttpContext.RequestServices.GetRequiredKeyedService<IDocuments>(kind);
        Kind = docs.Kind;
        Id = id;
        Doc = new LineForm { Sales = Kind.Sales, PriceField = Kind.PriceField, AccountField = Kind.AccountField };
        if (fromPost)
        {
            Doc.FromPost(Request.Form);
        }
        else if (id is { } i)
        {
            Doc.FromRecord(await docs.GetAsync(i), await docs.LinesAsync(i));
        }
        else
        {
            Doc.Header[Kind.DateField] = Format.Date(Format.Today);
            Doc.Header["currency_code"] = await lookups.BaseCurrencyAsync();
        }
        await Doc.LoadChoicesAsync(lookups);
    }

    public Task OnGetAsync(string kind, int? id) => LoadAsync(kind, id, fromPost: false);

    public async Task<IActionResult> OnPostAsync(string kind, int? id)
    {
        var docs = HttpContext.RequestServices.GetRequiredKeyedService<IDocuments>(kind);
        return await Attempt(async () =>
        {
            int saved;
            if (id is { } i)
            {
                await docs.UpdateAsync(i, Form());
                saved = i;
            }
            else
            {
                saved = await docs.CreateAsync(Form());
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
