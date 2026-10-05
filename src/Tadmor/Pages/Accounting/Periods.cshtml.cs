using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Accounting;

/// <summary>
/// Fiscal years, each with its periods, which close or reopen in one step
/// (spec/domain.md §13 A1); and, for administrators, closing and reopening
/// years (A2).
/// </summary>
public sealed class PeriodsModel(Calendar calendar, YearEnd yearEnd) : UiPage
{
    public List<Calendar.FiscalYearDto> Years { get; private set; } = [];
    public List<Calendar.PeriodDto> Periods { get; private set; } = [];
    /// <summary>The latest closed year, which is the only one that can be reopened.</summary>
    public int? ReopenableYear { get; private set; }

    private async Task<IActionResult> LoadAsync()
    {
        Years = await calendar.FiscalYearsAsync();
        Periods = await calendar.PeriodsAsync();
        ReopenableYear = Years.Where(y => y.Status == "closed").OrderByDescending(y => y.StartDate).Select(y => (int?)y.Id).FirstOrDefault();
        return Page();
    }

    public Task<IActionResult> OnGetAsync() => LoadAsync();

    public Task<IActionResult> OnPostToggleAsync(int period) =>
        Attempt(async () =>
        {
            var p = await calendar.PeriodAsync(period);
            var body = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                fiscal_year_id = p.FiscalYearId, name = p.Name, start_date = Format.Date(p.StartDate),
                end_date = Format.Date(p.EndDate), status = p.Status == "open" ? "closed" : "open",
            });
            await calendar.UpdatePeriodAsync(period, Input.From(body));
            Notice = $"Period {p.Name} {(p.Status == "open" ? "closed" : "reopened")}.";
            return Redirect("/periods");
        }, LoadAsync);

    public Task<IActionResult> OnPostReopenAsync(int year)
    {
        RequireAdmin();
        return Attempt(async () =>
        {
            var reversal = await yearEnd.ReopenAsync(year);
            Notice = reversal is null ? "Year reopened." : $"Year reopened; closing entry reversed by entry {reversal}.";
            return Redirect("/periods");
        }, LoadAsync);
    }
}
