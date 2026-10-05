using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Docs;

/// <summary>
/// An invoice, bill, or credit note (spec/domain.md §13 D3 to D7): its
/// lines and totals, the actions its state allows, what a credit note is
/// applied to, its PDF, and emailing it.
/// </summary>
public sealed class DetailModel(Lookups lookups, Settlement settlement, Printer printer) : UiPage
{
    public DocKind Kind { get; private set; } = null!;
    public int Id { get; private set; }
    public Dictionary<string, object?> Doc { get; private set; } = [];
    public List<Dictionary<string, object?>> Lines { get; private set; } = [];
    public List<Settlement.ApplicationDto> Applications { get; private set; } = [];
    public string PartyName { get; private set; } = "";
    public bool FromOrder => Lines.Any(l => l["order_line_id"] is not null);
    public EmailBox Email { get; private set; } = null!;
    private Dictionary<int, Db.Account> accounts = [];

    private IDocuments Docs(string kind) => HttpContext.RequestServices.GetRequiredKeyedService<IDocuments>(kind);

    private static SettlerKind? Settler(DocKind k) =>
        k == DocKind.SalesCreditNote ? SettlerKind.SalesCreditNote : k == DocKind.PurchaseCreditNote ? SettlerKind.PurchaseCreditNote : null;

    private async Task<IActionResult> LoadAsync(string kind, int id)
    {
        var docs = Docs(kind);
        Kind = docs.Kind;
        Id = id;
        Doc = await docs.GetAsync(id);
        Lines = await docs.LinesAsync(id);
        PartyName = (await lookups.PartyNamesAsync(Kind.Sales)).GetValueOrDefault((int)Doc[Kind.PartyField]!, "");
        accounts = await lookups.AccountMapAsync();
        if (Settler(Kind) is { } s)
        {
            Applications = await settlement.ApplicationsAsync(s, id);
        }
        Email ??= new EmailBox();
        return Page();
    }

    public string AccountLabel(object? id) => id is int i && accounts.TryGetValue(i, out var a) ? $"{a.Code} {a.Name}" : "";

    public Task<IActionResult> OnGetAsync(string kind, int id) => LoadAsync(kind, id);

    private Task<IActionResult> Act(string kind, int id, Func<IDocuments, Task<string>> action) =>
        Attempt(async () =>
        {
            Notice = await action(Docs(kind));
            return Redirect($"/{kind}/{id}");
        }, () => LoadAsync(kind, id));

    public Task<IActionResult> OnPostPostAsync(string kind, int id) =>
        Act(kind, id, async d => $"Posted as journal entry {await d.PostAsync(id)}.");

    public Task<IActionResult> OnPostUnpostAsync(string kind, int id)
    {
        RequireAdmin();
        return Act(kind, id, async d => $"Unposted; reversal entry {await d.UnpostAsync(id)}.");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string kind, int id) =>
        await Attempt(async () =>
        {
            await Docs(kind).DeleteAsync(id);
            Notice = "Deleted.";
            return Redirect($"/{kind}");
        }, () => LoadAsync(kind, id));

    public Task<IActionResult> OnPostApplyAsync(string kind, int id) =>
        Act(kind, id, async d =>
        {
            var created = await settlement.ApplyAsync(Settler(d.Kind) ?? throw ServiceException.NotFound(), id);
            return created.Count == 0 ? "Nothing was open to apply to." : $"Applied to {created.Count} document(s).";
        });

    public async Task<IActionResult> OnPostEmailAsync(string kind, int id)
    {
        var printKind = PrintKind.All.Single(p => p.Collection == kind);
        Email = new EmailBox { To = Request.Form["to"] };
        try
        {
            var to = await printer.EmailAsync(printKind, id, Form("to"));
            Email.Sent = string.Join(", ", to);
        }
        catch (ServiceException e) when (e.Status != 404)
        {
            Email.Error = e.Message;
        }
        return await LoadAsync(kind, id);
    }
}

/// <summary>The email form's state: what was typed, and the outcome (spec/domain.md §13 D7).</summary>
public sealed class EmailBox
{
    public string? To { get; set; }
    public string? Sent { get; set; }
    public string? Error { get; set; }
}
