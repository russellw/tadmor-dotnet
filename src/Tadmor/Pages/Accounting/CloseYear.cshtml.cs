using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Accounting;

/// <summary>
/// The year-end close (spec/domain.md §13 A2), administrators only: it asks
/// for the retained-earnings account, proposing the seeded Retained
/// Earnings, and says what will happen before doing it.
/// </summary>
public sealed class CloseYearModel(Calendar calendar, YearEnd yearEnd, Lookups lookups) : UiPage
{
    public Calendar.FiscalYearDto Year { get; private set; } = null!;
    public List<Option> EquityAccounts { get; private set; } = [];
    public string? Proposed { get; private set; }

    private async Task<IActionResult> LoadAsync(int id)
    {
        RequireAdmin();
        Year = await calendar.FiscalYearAsync(id);
        EquityAccounts = await lookups.PostableAccountsAsync(a => a.AccountType == "equity");
        Proposed = (await lookups.AccountsAsync()).FirstOrDefault(a => a.Code == "3000")?.Id.ToString();
        return Page();
    }

    public Task<IActionResult> OnGetAsync(int id) => LoadAsync(id);

    public async Task<IActionResult> OnPostAsync(int id)
    {
        RequireAdmin();
        return await Attempt(async () =>
        {
            var (closing, next) = await yearEnd.CloseAsync(id, Form());
            Notice = "Year closed." + (closing is { } c ? $" Closing entry {c}." : " There was nothing to sweep.")
                + (next is not null ? " The next fiscal year was created." : "");
            return Redirect("/periods");
        }, () => LoadAsync(id));
    }
}
