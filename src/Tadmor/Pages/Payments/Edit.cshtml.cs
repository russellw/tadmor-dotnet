using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Payments;

/// <summary>The payment form (spec/domain.md §13 P2), drafts only.</summary>
public sealed class EditModel(Lookups lookups) : UiPage
{
    public PayKind Kind { get; private set; } = null!;
    public int? Id { get; private set; }
    public Dictionary<string, string> Values { get; } = [];
    public List<Lookups.PartyOption> Parties { get; private set; } = [];
    public List<Option> Currencies { get; private set; } = [];
    public List<Option> CashAccounts { get; private set; } = [];

    private async Task LoadAsync(string kind, int? id, bool fromPost)
    {
        var payments = HttpContext.RequestServices.GetRequiredKeyedService<IPayments>(kind);
        Kind = payments.Kind;
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
            foreach (var (k, v) in await payments.GetAsync(i))
            {
                Values[k] = k == "amount" ? Format.Qty(Format.Dec(v)) : Format.Raw(v);
            }
        }
        else
        {
            Values["payment_date"] = Format.Date(Format.Today);
            Values["currency_code"] = await lookups.BaseCurrencyAsync();
        }
        Parties = (await lookups.PartiesAsync(Kind.Sales)).Where(p => p.IsActive || p.Id.ToString() == Values.GetValueOrDefault(Kind.PartyField)).ToList();
        Currencies = await lookups.CurrencyOptionsAsync();
        // Cash and bank accounts first, then any other postable account.
        var accounts = await lookups.AccountsAsync();
        CashAccounts = accounts.Where(a => a.IsPostable && a.IsActive).OrderBy(a => !a.IsCash).ThenBy(a => a.Code)
            .Select(a => new Option(a.Id.ToString(), $"{a.Code} {a.Name}")).ToList();
    }

    public Task OnGetAsync(string kind, int? id) => LoadAsync(kind, id, fromPost: false);

    public async Task<IActionResult> OnPostAsync(string kind, int? id)
    {
        var payments = HttpContext.RequestServices.GetRequiredKeyedService<IPayments>(kind);
        return await Attempt(async () =>
        {
            var saved = id ?? 0;
            if (id is { } i)
            {
                await payments.UpdateAsync(i, Form());
            }
            else
            {
                saved = await payments.CreateAsync(Form());
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
