using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Payments;

/// <summary>A payment, its actions, and what it is applied to (spec/domain.md §13 P3, P4).</summary>
public sealed class DetailModel(Lookups lookups, Settlement settlement) : UiPage
{
    public PayKind Kind { get; private set; } = null!;
    public int Id { get; private set; }
    public Dictionary<string, object?> Payment { get; private set; } = [];
    public List<Settlement.ApplicationDto> Applications { get; private set; } = [];
    public string PartyName { get; private set; } = "";
    public string CashAccount { get; private set; } = "";

    private IPayments Payments(string kind) => HttpContext.RequestServices.GetRequiredKeyedService<IPayments>(kind);

    private async Task<IActionResult> LoadAsync(string kind, int id)
    {
        var payments = Payments(kind);
        Kind = payments.Kind;
        Id = id;
        Payment = await payments.GetAsync(id);
        PartyName = (await lookups.PartyNamesAsync(Kind.Sales)).GetValueOrDefault((int)Payment[Kind.PartyField]!, "");
        CashAccount = await lookups.AccountLabelAsync(Payment[Kind.CashField] as int?);
        Applications = await settlement.ApplicationsAsync(Kind.Settler, id);
        return Page();
    }

    public Task<IActionResult> OnGetAsync(string kind, int id) => LoadAsync(kind, id);

    private Task<IActionResult> Act(string kind, int id, Func<IPayments, Task<string>> action) =>
        Attempt(async () =>
        {
            Notice = await action(Payments(kind));
            return Redirect($"/{kind}/{id}");
        }, () => LoadAsync(kind, id));

    public Task<IActionResult> OnPostPostAsync(string kind, int id) =>
        Act(kind, id, async p => $"Posted as journal entry {await p.PostAsync(id)}.");

    public Task<IActionResult> OnPostUnpostAsync(string kind, int id)
    {
        RequireAdmin();
        return Act(kind, id, async p => $"Unposted; reversal entry {await p.UnpostAsync(id)}.");
    }

    public Task<IActionResult> OnPostApplyAsync(string kind, int id) =>
        Act(kind, id, async p =>
        {
            var created = await settlement.ApplyAsync(p.Kind.Settler, id);
            return created.Count == 0 ? "Nothing was open to apply to." : $"Applied to {created.Count} document(s).";
        });

    public Task<IActionResult> OnPostDeleteAsync(string kind, int id) =>
        Attempt(async () =>
        {
            await Payments(kind).DeleteAsync(id);
            Notice = "Deleted.";
            return Redirect($"/{kind}");
        }, () => LoadAsync(kind, id));
}
