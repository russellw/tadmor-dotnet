using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Bank;

/// <summary>The statement form, offering only cash accounts (spec/domain.md §13 A4).</summary>
public sealed class EditModel(Banking banking, Lookups lookups) : UiPage
{
    public string Key { get; private set; } = "";
    public bool IsNew => Key == "new";
    public List<Option> CashAccounts { get; private set; } = [];
    private readonly Dictionary<string, string> values = [];

    public string V(string n) => Request.HasFormContentType ? Request.Form[n].ToString() : values.GetValueOrDefault(n, "");

    private async Task<IActionResult> LoadAsync(int? id)
    {
        Key = id?.ToString() ?? "new";
        if (!IsNew)
        {
            var s = await banking.GetAsync(id!.Value);
            values["account_id"] = s.AccountId.ToString();
            values["statement_date"] = Format.Date(s.StatementDate);
            values["opening_balance"] = Format.Qty(s.OpeningBalance);
            values["closing_balance"] = Format.Qty(s.ClosingBalance);
            values["reference"] = s.Reference ?? "";
        }
        CashAccounts = await lookups.PostableAccountsAsync(a => a.IsCash);
        return Page();
    }

    public Task<IActionResult> OnGetAsync(int? id) => LoadAsync(id);

    public Task<IActionResult> OnPostAsync(int? id) =>
        Attempt(async () =>
        {
            var saved = id ?? await banking.CreateAsync(Form());
            if (id is not null)
            {
                await banking.UpdateAsync(saved, Form());
            }
            Notice = "Saved.";
            return Redirect($"/bank-statements/{saved}");
        }, () => LoadAsync(id));
}
