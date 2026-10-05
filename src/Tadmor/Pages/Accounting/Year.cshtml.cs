using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Accounting;

/// <summary>The fiscal year form (spec/domain.md §13 A1).</summary>
public sealed class YearModel(Calendar calendar) : UiPage
{
    public string Key { get; private set; } = "";
    private readonly Dictionary<string, string> values = [];

    public string V(string n) => Request.HasFormContentType ? Request.Form[n].ToString() : values.GetValueOrDefault(n, "");

    private async Task<IActionResult> LoadAsync(string key)
    {
        Key = key;
        if (key != "new")
        {
            var y = await calendar.FiscalYearAsync(PathId(key));
            values["name"] = y.Name;
            values["start_date"] = Format.Date(y.StartDate);
            values["end_date"] = Format.Date(y.EndDate);
            values["status"] = y.Status;
        }
        return Page();
    }

    public Task<IActionResult> OnGetAsync(string key) => LoadAsync(key);

    public Task<IActionResult> OnPostAsync(string key) =>
        Attempt(async () =>
        {
            if (key == "new")
            {
                await calendar.CreateFiscalYearAsync(Form());
            }
            else
            {
                await calendar.UpdateFiscalYearAsync(PathId(key), Form());
            }
            Notice = "Saved.";
            return Redirect("/periods");
        }, () => LoadAsync(key));
}
