using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Accounting;

/// <summary>Exchange rates: a list, a form to create and change a rate, and delete (spec/domain.md §13 A3).</summary>
public sealed class RatesModel(Ledger ledger, Lookups lookups) : UiPage
{
    public List<Ledger.RateDto> Rates { get; private set; } = [];
    public List<Option> Currencies { get; private set; } = [];
    public string Base { get; private set; } = "";

    private async Task<IActionResult> LoadAsync()
    {
        Rates = await ledger.RatesAsync();
        Currencies = await lookups.CurrencyOptionsAsync();
        Base = await lookups.BaseCurrencyAsync();
        return Page();
    }

    public Task<IActionResult> OnGetAsync() => LoadAsync();

    private static DateOnly Day(string date) => Input.TryParseDate(date) ?? throw ServiceException.NotFound();

    public Task<IActionResult> OnPostCreateAsync() =>
        Attempt(async () =>
        {
            await ledger.CreateRateAsync(Form());
            Notice = "Rate added.";
            return Redirect("/exchange-rates");
        }, LoadAsync);

    public Task<IActionResult> OnPostUpdateAsync(string currency, string date) =>
        Attempt(async () =>
        {
            await ledger.UpdateRateAsync(currency, Day(date), Form());
            Notice = "Rate changed.";
            return Redirect("/exchange-rates");
        }, LoadAsync);

    public Task<IActionResult> OnPostDeleteAsync(string currency, string date) =>
        Attempt(async () =>
        {
            await ledger.DeleteRateAsync(currency, Day(date));
            Notice = "Rate deleted.";
            return Redirect("/exchange-rates");
        }, LoadAsync);
}
