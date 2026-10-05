using Microsoft.AspNetCore.Mvc;
using Tadmor.Pages.Docs;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Orders;

/// <summary>
/// An order (spec/domain.md §13 O3, O4, O7): its lines with what has been
/// fulfilled and what remains on each axis, and the actions its state allows.
/// </summary>
public sealed class DetailModel(Lookups lookups, Printer printer) : UiPage
{
    public OrderKind Kind { get; private set; } = null!;
    public int Id { get; private set; }
    public Dictionary<string, object?> Order { get; private set; } = [];
    public List<Dictionary<string, object?>> Lines { get; private set; } = [];
    public string PartyName { get; private set; } = "";
    public EmailBox Email { get; private set; } = new();

    private IOrders Orders(string kind) => HttpContext.RequestServices.GetRequiredKeyedService<IOrders>(kind);

    private async Task<IActionResult> LoadAsync(string kind, int id)
    {
        var orders = Orders(kind);
        Kind = orders.Kind;
        Id = id;
        Order = await orders.GetAsync(id);
        Lines = await orders.LinesAsync(id);
        PartyName = (await lookups.PartyNamesAsync(Kind.Sales)).GetValueOrDefault((int)Order[Kind.PartyField]!, "");
        return Page();
    }

    public Task<IActionResult> OnGetAsync(string kind, int id) => LoadAsync(kind, id);

    private Task<IActionResult> Act(string kind, int id, Func<IOrders, Task> action, string done) =>
        Attempt(async () =>
        {
            await action(Orders(kind));
            Notice = done;
            return Redirect($"/{kind}/{id}");
        }, () => LoadAsync(kind, id));

    public Task<IActionResult> OnPostConfirmAsync(string kind, int id) => Act(kind, id, o => o.ConfirmAsync(id), "Confirmed.");

    public Task<IActionResult> OnPostCloseAsync(string kind, int id) => Act(kind, id, o => o.CloseAsync(id), "Closed.");

    public Task<IActionResult> OnPostCancelAsync(string kind, int id) => Act(kind, id, o => o.CancelAsync(id), "Cancelled.");

    public Task<IActionResult> OnPostDeleteAsync(string kind, int id) =>
        Attempt(async () =>
        {
            await Orders(kind).DeleteAsync(id);
            Notice = "Deleted.";
            return Redirect($"/{kind}");
        }, () => LoadAsync(kind, id));

    public async Task<IActionResult> OnPostEmailAsync(string kind, int id)
    {
        Email = new EmailBox { To = Request.Form["to"] };
        try
        {
            Email.Sent = string.Join(", ", await printer.EmailAsync(PrintKind.All.Single(p => p.Collection == kind), id, Form("to")));
        }
        catch (ServiceException e) when (e.Status != 404)
        {
            Email.Error = e.Message;
        }
        return await LoadAsync(kind, id);
    }
}
