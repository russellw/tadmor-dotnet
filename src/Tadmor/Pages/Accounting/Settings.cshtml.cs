using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Accounting;

/// <summary>Ledger settings, read-only for non-administrators (spec/domain.md §13 M8).</summary>
public sealed class SettingsModel(Ledger ledger, Lookups lookups) : UiPage
{
    public List<Option> Currencies { get; private set; } = [];
    public List<Option> Accounts { get; private set; } = [];
    public string FxLabel { get; private set; } = "";
    private readonly Dictionary<string, string> values = [];

    public string V(string n) => Request.HasFormContentType ? Request.Form[n].ToString() : values.GetValueOrDefault(n, "");

    private async Task<IActionResult> LoadAsync()
    {
        var s = await ledger.SettingsAsync();
        values["base_currency"] = s.BaseCurrency;
        values["fx_gain_loss_account_id"] = s.FxGainLossAccountId?.ToString() ?? "";
        Currencies = await lookups.CurrencyOptionsAsync();
        Accounts = await lookups.PostableAccountsAsync();
        FxLabel = await lookups.AccountLabelAsync(s.FxGainLossAccountId);
        return Page();
    }

    public Task<IActionResult> OnGetAsync() => LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        RequireAdmin();
        return await Attempt(async () =>
        {
            await ledger.UpdateSettingsAsync(Form());
            Notice = "Settings saved.";
            return Redirect("/settings");
        }, LoadAsync);
    }
}
