using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Accounting;

/// <summary>
/// The period form (spec/domain.md §13 A1). A new period is proposed as
/// the calendar month after the latest existing period.
/// </summary>
public sealed class PeriodModel(Calendar calendar) : UiPage
{
    public string Key { get; private set; } = "";
    public List<Calendar.FiscalYearDto> Years { get; private set; } = [];
    private readonly Dictionary<string, string> values = [];

    public string V(string n) => Request.HasFormContentType ? Request.Form[n].ToString() : values.GetValueOrDefault(n, "");

    private async Task<IActionResult> LoadAsync(string key)
    {
        Key = key;
        Years = await calendar.FiscalYearsAsync();
        if (key != "new")
        {
            var p = await calendar.PeriodAsync(PathId(key));
            values["fiscal_year_id"] = p.FiscalYearId.ToString();
            values["name"] = p.Name;
            values["start_date"] = Format.Date(p.StartDate);
            values["end_date"] = Format.Date(p.EndDate);
            values["status"] = p.Status;
        }
        else if ((await calendar.PeriodsAsync()).OrderBy(p => p.EndDate).LastOrDefault() is { } latest)
        {
            var start = latest.EndDate.AddDays(1);
            var end = new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
            values["name"] = start.ToString("yyyy-MM");
            values["start_date"] = Format.Date(start);
            values["end_date"] = Format.Date(end);
            values["fiscal_year_id"] = Years.FirstOrDefault(y => y.StartDate <= start && y.EndDate >= start)?.Id.ToString() ?? "";
        }
        return Page();
    }

    public Task<IActionResult> OnGetAsync(string key) => LoadAsync(key);

    public Task<IActionResult> OnPostAsync(string key) =>
        Attempt(async () =>
        {
            if (key == "new")
            {
                await calendar.CreatePeriodAsync(Form());
            }
            else
            {
                await calendar.UpdatePeriodAsync(PathId(key), Form());
            }
            Notice = "Saved.";
            return Redirect("/periods");
        }, () => LoadAsync(key));
}
