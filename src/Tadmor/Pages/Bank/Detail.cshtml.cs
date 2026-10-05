using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Bank;

/// <summary>
/// A statement's reconciliation (spec/domain.md §13 A5): its lines and
/// their matches; while open, lines added by hand or pasted as CSV, matched
/// from the candidates of their amount (or all candidates), unmatched, and
/// deleted; auto-match; and reconciling. Administrators may reopen.
/// </summary>
public sealed class DetailModel(Banking banking) : UiPage
{
    public Banking.StatementRow Statement { get; private set; } = null!;
    public List<Banking.LineRow> Lines { get; private set; } = [];
    public List<Banking.Candidate> Candidates { get; private set; } = [];
    public bool All => Request.Query["all"] == "1";

    private async Task<IActionResult> LoadAsync(int id)
    {
        Statement = await banking.GetAsync(id);
        Lines = await banking.LinesAsync(id);
        Candidates = Statement.Status == "open" ? await banking.CandidatesAsync(id) : [];
        return Page();
    }

    public Task<IActionResult> OnGetAsync(int id) => LoadAsync(id);

    private Task<IActionResult> Act(int id, Func<Task<string>> action, string? then = null) =>
        Attempt(async () =>
        {
            Notice = await action();
            return Redirect(then ?? $"/bank-statements/{id}");
        }, () => LoadAsync(id));

    public Task<IActionResult> OnPostAutomatchAsync(int id) => Act(id, async () => $"Matched {await banking.AutoMatchAsync(id)} line(s).");

    public Task<IActionResult> OnPostReconcileAsync(int id) => Act(id, async () =>
    {
        await banking.ReconcileAsync(id);
        return "Reconciled.";
    });

    public Task<IActionResult> OnPostReopenAsync(int id)
    {
        RequireAdmin();
        return Act(id, async () =>
        {
            await banking.ReopenAsync(id);
            return "Reopened.";
        });
    }

    public Task<IActionResult> OnPostDeleteAsync(int id) => Act(id, async () =>
    {
        await banking.DeleteAsync(id);
        return "Statement deleted.";
    }, "/bank-statements");

    public Task<IActionResult> OnPostAddlineAsync(int id) => Act(id, async () =>
    {
        await banking.AddLineAsync(id, Form());
        return "Line added.";
    });

    public Task<IActionResult> OnPostImportAsync(int id) => Act(id, async () => $"Imported {await banking.ImportAsync(id, Form())} line(s).");

    public Task<IActionResult> OnPostMatchAsync(int id, int line) => Act(id, async () =>
    {
        await banking.MatchAsync(line, Form());
        return "Matched.";
    });

    public Task<IActionResult> OnPostUnmatchAsync(int id, int line) => Act(id, async () =>
    {
        await banking.UnmatchAsync(line);
        return "Unmatched.";
    });

    public Task<IActionResult> OnPostDeletelineAsync(int id, int line) => Act(id, async () =>
    {
        await banking.DeleteLineAsync(line);
        return "Line deleted.";
    });
}
