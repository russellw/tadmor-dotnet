using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Bank;

/// <summary>Bank statements, newest first (spec/domain.md §13 A4).</summary>
public sealed class ListModel(Banking banking) : UiPage
{
    public List<Banking.StatementRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => Rows = await banking.ListAsync();
}
