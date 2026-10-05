using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Reports;

/// <summary>An account's ledger with a running balance (spec/domain.md §13 R5).</summary>
public sealed class LedgerModel(Services.Reports reports, Lookups lookups) : UiPage
{
    public string AccountLabel { get; private set; } = "";
    public string Base { get; private set; } = "";
    public List<Services.Reports.LedgerRow> Rows { get; private set; } = [];

    private DateOnly? Date(string name)
    {
        var s = Request.Query[name].ToString().Trim();
        if (s.Length == 0)
        {
            return null;
        }
        if (Input.TryParseDate(s) is { } d)
        {
            return d;
        }
        Error = $"{s} is not a date; showing the ledger without that bound.";
        return null;
    }

    public async Task OnGetAsync(int id)
    {
        Rows = await reports.LedgerAsync(id, Date("from"), Date("to"));
        AccountLabel = await lookups.AccountLabelAsync(id);
        Base = await lookups.BaseCurrencyAsync();
    }
}
