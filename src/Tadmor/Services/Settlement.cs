using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>
/// A settling document, a payment or a credit note, and where its
/// applications go (spec/domain.md §5). SQL identifiers are constants.
/// </summary>
public sealed record SettlerKind
{
    public required string Table { get; init; }
    public required string DateColumn { get; init; }
    /// <summary>What the settler is worth: a payment's amount, a credit note's total.</summary>
    public required string AmountColumn { get; init; }
    public required string PartyColumn { get; init; }
    public required string ApplicationTable { get; init; }
    public required string ApplicationSettler { get; init; }
    public required string ApplicationDocument { get; init; }
    public required string DocumentTable { get; init; }
    public required string DocumentDate { get; init; }
    public required string DocumentNumber { get; init; }
    /// <summary>The schema's function totaling everything applied to a document.</summary>
    public required string SettledFunction { get; init; }
    public required string PartyTable { get; init; }
    public required string ControlColumn { get; init; }
    public required bool Sales { get; init; }
    public required string Label { get; init; }

    public static readonly SettlerKind CustomerPayment = new()
    {
        Table = "customer_payments", DateColumn = "payment_date", AmountColumn = "amount", PartyColumn = "customer_id",
        ApplicationTable = "payment_applications", ApplicationSettler = "payment_id", ApplicationDocument = "invoice_id",
        DocumentTable = "sales_invoices", DocumentDate = "invoice_date", DocumentNumber = "invoice_number",
        SettledFunction = "invoice_amount_settled", PartyTable = "customers", ControlColumn = "ar_account_id", Sales = true,
        Label = "Payment",
    };

    public static readonly SettlerKind SupplierPayment = new()
    {
        Table = "supplier_payments", DateColumn = "payment_date", AmountColumn = "amount", PartyColumn = "supplier_id",
        ApplicationTable = "bill_applications", ApplicationSettler = "payment_id", ApplicationDocument = "bill_id",
        DocumentTable = "purchase_bills", DocumentDate = "bill_date", DocumentNumber = "bill_number",
        SettledFunction = "bill_amount_settled", PartyTable = "suppliers", ControlColumn = "ap_account_id", Sales = false,
        Label = "Payment",
    };

    public static readonly SettlerKind SalesCreditNote = new()
    {
        Table = "sales_credit_notes", DateColumn = "credit_note_date", AmountColumn = "total", PartyColumn = "customer_id",
        ApplicationTable = "sales_credit_applications", ApplicationSettler = "credit_note_id", ApplicationDocument = "invoice_id",
        DocumentTable = "sales_invoices", DocumentDate = "invoice_date", DocumentNumber = "invoice_number",
        SettledFunction = "invoice_amount_settled", PartyTable = "customers", ControlColumn = "ar_account_id", Sales = true,
        Label = "Credit note",
    };

    public static readonly SettlerKind PurchaseCreditNote = new()
    {
        Table = "purchase_credit_notes", DateColumn = "credit_note_date", AmountColumn = "total", PartyColumn = "supplier_id",
        ApplicationTable = "purchase_credit_applications", ApplicationSettler = "credit_note_id", ApplicationDocument = "bill_id",
        DocumentTable = "purchase_bills", DocumentDate = "bill_date", DocumentNumber = "bill_number",
        SettledFunction = "bill_amount_settled", PartyTable = "suppliers", ControlColumn = "ap_account_id", Sales = false,
        Label = "Credit note",
    };
}

/// <summary>
/// Applications: allocating a posted payment or credit note to the party's
/// open invoices or bills in the same currency (spec/domain.md §5), and the
/// realized-FX entries that settling at a different rate posts (§7.3).
/// </summary>
public sealed class Settlement(TadmorDb db, Journal journal)
{
    public sealed record ApplicationDto(int DocumentId, string DocumentNumber, decimal AmountApplied);

    public sealed class SettlerRow
    {
        public string Status { get; set; } = "";
        public int PartyId { get; set; }
        public string CurrencyCode { get; set; } = "";
        public decimal Amount { get; set; }
        public DateOnly Date { get; set; }
        public decimal? Rate { get; set; }
        public decimal Applied { get; set; }
    }

    public sealed class OpenDocument
    {
        public int Id { get; set; }
        public string Number { get; set; } = "";
        public decimal Available { get; set; }
        public decimal Rate { get; set; }
    }

    /// <summary>The settler's applications, in creation order; 404 if it does not exist.</summary>
    public async Task<List<ApplicationDto>> ApplicationsAsync(SettlerKind k, int id)
    {
        if (await db.ScalarAsync<int?>($"SELECT id FROM {k.Table} WHERE id = {{0}}", id) is null)
        {
            throw ServiceException.NotFound();
        }
        return await db.ListAsync<ApplicationDto>($$"""
            SELECT a.{{k.ApplicationDocument}} AS "DocumentId", d.{{k.DocumentNumber}} AS "DocumentNumber",
                a.amount_applied AS "AmountApplied"
            FROM {{k.ApplicationTable}} a JOIN {{k.DocumentTable}} d ON d.id = a.{{k.ApplicationDocument}}
            WHERE a.{{k.ApplicationSettler}} = {0} ORDER BY a.id
            """, id);
    }

    /// <summary>
    /// Allocates the settler's unapplied remainder across the party's open
    /// documents, oldest first by date then id, each receiving
    /// min(available, remaining), and returns the applications created
    /// (spec/domain.md §5.1). The settler must be posted (409). A realized FX
    /// difference with no FX account configured refuses the whole apply
    /// (422), and nothing is created.
    /// </summary>
    public async Task<List<ApplicationDto>> ApplyAsync(SettlerKind k, int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = (await db.ListAsync<SettlerRow>($$"""
            SELECT s.status AS "Status", s.{{k.PartyColumn}} AS "PartyId", s.currency_code AS "CurrencyCode",
                s.{{k.AmountColumn}} AS "Amount", s.{{k.DateColumn}} AS "Date", je.exchange_rate AS "Rate",
                COALESCE((SELECT sum(amount_applied) FROM {{k.ApplicationTable}} WHERE {{k.ApplicationSettler}} = s.id), 0) AS "Applied"
            FROM {{k.Table}} s LEFT JOIN journal_entries je ON je.id = s.journal_entry_id
            WHERE s.id = {0} FOR UPDATE OF s
            """, id)).SingleOrDefault() ?? throw ServiceException.NotFound();
        if (s.Status != "posted" || s.Rate is not { } settlerRate)
        {
            throw ServiceException.Conflict($"{k.Label} {id} is {s.Status}; only a posted one can be applied");
        }

        var remaining = s.Amount - s.Applied;
        var created = new List<ApplicationDto>();
        if (remaining <= 0)
        {
            return created;
        }
        var open = await db.ListAsync<OpenDocument>($$"""
            SELECT d.id AS "Id", d.{{k.DocumentNumber}} AS "Number",
                d.total - {{k.SettledFunction}}(d.id) AS "Available", je.exchange_rate AS "Rate"
            FROM {{k.DocumentTable}} d JOIN journal_entries je ON je.id = d.journal_entry_id
            WHERE d.{{k.PartyColumn}} = {0} AND d.currency_code = {1} AND d.status = 'posted'
              AND d.total - {{k.SettledFunction}}(d.id) > 0
            ORDER BY d.{{k.DocumentDate}}, d.id
            FOR UPDATE OF d
            """, s.PartyId, s.CurrencyCode);

        foreach (var d in open)
        {
            if (remaining <= 0)
            {
                break;
            }
            var amount = Math.Min(d.Available, remaining);
            remaining -= amount;
            var application = await db.ScalarAsync<int>($$"""
                INSERT INTO {{k.ApplicationTable}} ({{k.ApplicationSettler}}, {{k.ApplicationDocument}}, amount_applied)
                VALUES ({0}, {1}, {2}) RETURNING id
                """, id, d.Id, amount);
            var diff = await db.ScalarAsync<decimal>("SELECT round({0}::numeric * {1}::numeric, 4) - round({0}::numeric * {2}::numeric, 4)",
                amount, settlerRate, d.Rate);
            if (diff != 0)
            {
                var fx = await PostFxAsync(k, s, diff);
                await db.ExecAsync($"UPDATE {k.ApplicationTable} SET fx_journal_entry_id = {{0}} WHERE id = {{1}}", fx, application);
            }
            created.Add(new ApplicationDto(d.Id, d.Number, amount));
        }
        await tx.CommitAsync();
        return created;
    }

    /// <summary>
    /// Books a realized FX difference: in the base currency, dated on the
    /// settler's date, between the party's control account and the FX
    /// gain/loss account. On the customer side a positive difference is a
    /// gain (Dr A/R, Cr FX); on the supplier side a loss (Cr A/P, Dr FX).
    /// </summary>
    private async Task<int> PostFxAsync(SettlerKind k, SettlerRow s, decimal diff)
    {
        var fxAccount = await db.GlSettings.Select(g => g.FxGainLossAccountId).SingleAsync()
            ?? throw ServiceException.Unprocessable("a realized FX difference arises, and no FX gain/loss account is configured");
        var control = await db.ScalarAsync<int?>($"SELECT {k.ControlColumn} FROM {k.PartyTable} WHERE id = {{0}}", s.PartyId)
            ?? throw ServiceException.Unprocessable($"the {(k.Sales ? "customer has no A/R" : "supplier has no A/P")} account");
        var period = await journal.PeriodForAsync(s.Date);
        var entry = await journal.CreateEntryAsync(s.Date, period, await journal.BaseCurrencyAsync(), "Realized exchange difference", null);
        var controlAmount = k.Sales ? diff : -diff;
        await journal.AddLineAsync(entry, control, controlAmount, "Realized FX");
        await journal.AddLineAsync(entry, fxAccount, -controlAmount, "Realized FX");
        return entry;
    }

    /// <summary>
    /// Removes a payment's applications, reversing the FX entries they
    /// posted, when the payment is unposted (spec/domain.md §4.4).
    /// </summary>
    public async Task RemoveApplicationsAsync(SettlerKind k, int id)
    {
        var fxEntries = await db.ListAsync<int>(
            $"SELECT fx_journal_entry_id FROM {k.ApplicationTable} WHERE {k.ApplicationSettler} = {{0}} AND fx_journal_entry_id IS NOT NULL ORDER BY id", id);
        await db.ExecAsync($"DELETE FROM {k.ApplicationTable} WHERE {k.ApplicationSettler} = {{0}}", id);
        foreach (var fx in fxEntries)
        {
            await journal.ReverseAsync(fx);
        }
    }
}
