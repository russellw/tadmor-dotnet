using Tadmor.Services;
using Tadmor.Ui;
using StockService = Tadmor.Services.Stock;

namespace Tadmor.Pages.Reports;

/// <summary>
/// The financial reports (spec/domain.md §13 R1 to R4, R7, R8). Every
/// figure is in the base currency except aging, which is per document.
/// </summary>
public sealed class ReportModel(Services.Reports reports, StockService stock, Lookups lookups) : UiPage
{
    public string Name { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Base { get; private set; } = "";
    public List<Services.Reports.ActivityRow>? Pnl { get; private set; }
    public decimal NetIncome { get; private set; }
    public Services.Reports.BalanceSheetDto? BalanceSheet { get; private set; }
    public Services.Reports.CashFlowDto? CashFlow { get; private set; }
    public List<Services.Reports.TrialBalanceRow>? TrialBalance { get; private set; }
    public List<Services.Reports.AgingRow>? Aging { get; private set; }
    public List<StockService.ValuationRow>? Valuation { get; private set; }

    /// <summary>A date filter; a malformed one is reported and treated as blank.</summary>
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
        Error = $"{s} is not a date; showing the report without that bound.";
        return null;
    }

    public async Task OnGetAsync(string name)
    {
        Name = name;
        Base = await lookups.BaseCurrencyAsync();
        switch (name)
        {
            case "profit-and-loss":
                Title = "Profit and loss";
                Pnl = await reports.ProfitAndLossAsync(Date("from"), Date("to"));
                NetIncome = Pnl.Where(r => r.AccountType == "revenue").Sum(r => r.Amount) - Pnl.Where(r => r.AccountType == "expense").Sum(r => r.Amount);
                break;
            case "balance-sheet":
                Title = "Balance sheet";
                BalanceSheet = await reports.BalanceSheetAsync(Date("as_of"));
                break;
            case "cash-flow":
                Title = "Cash flow";
                CashFlow = await reports.CashFlowAsync(Date("from"), Date("to"));
                break;
            case "trial-balance":
                Title = "Trial balance";
                TrialBalance = await reports.TrialBalanceAsync();
                break;
            case "ar-aging":
                Title = "Accounts receivable aging";
                Aging = await reports.AgingAsync(receivables: true);
                break;
            case "ap-aging":
                Title = "Accounts payable aging";
                Aging = await reports.AgingAsync(receivables: false);
                break;
            default:
                Title = "Inventory valuation";
                Valuation = await stock.ValuationAsync();
                break;
        }
    }
}
