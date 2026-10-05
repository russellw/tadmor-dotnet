using Tadmor.Ui;

namespace Tadmor.Pages;

/// <summary>The home screen (spec/domain.md §13 H1 to H5).</summary>
public sealed class IndexModel(Dashboard dashboard) : UiPage
{
    public List<Dashboard.Outstanding> Receivables { get; private set; } = [];
    public List<Dashboard.Outstanding> Payables { get; private set; } = [];
    public Dashboard.Counts Counts { get; private set; } = new();
    public List<Dashboard.Due> Overdue { get; private set; } = [];
    public List<Dashboard.Due> DueSoon { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Receivables = await dashboard.OutstandingAsync(receivables: true);
        Payables = await dashboard.OutstandingAsync(receivables: false);
        Counts = await dashboard.CountsAsync();
        Overdue = await dashboard.OverdueInvoicesAsync();
        DueSoon = await dashboard.BillsDueSoonAsync();
    }
}
