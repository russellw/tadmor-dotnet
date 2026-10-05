using Tadmor.Ui;

namespace Tadmor.Pages.Reports;

/// <summary>A journal entry with its lines and totals in both currencies (spec/domain.md §13 R6).</summary>
public sealed class EntryModel(Services.Reports reports, Lookups lookups) : UiPage
{
    public Services.Reports.EntryDto Entry { get; private set; } = null!;
    public string Base { get; private set; } = "";

    public async Task OnGetAsync(int id)
    {
        Entry = await reports.EntryAsync(id);
        Base = await lookups.BaseCurrencyAsync();
    }
}
