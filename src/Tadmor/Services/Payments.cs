using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>What distinguishes customer and supplier payments (spec/api.md §5.9).</summary>
public sealed record PayKind
{
    public required string Collection { get; init; }
    public required string Table { get; init; }
    public required string PartyField { get; init; }
    public required string CashField { get; init; }
    public required string PartyTable { get; init; }
    public required string ControlColumn { get; init; }
    public required bool Sales { get; init; }
    public required SettlerKind Settler { get; init; }

    public static readonly PayKind Customer = new()
    {
        Collection = "customer-payments", Table = "customer_payments", PartyField = "customer_id",
        CashField = "deposit_account_id", PartyTable = "customers", ControlColumn = "ar_account_id", Sales = true,
        Settler = SettlerKind.CustomerPayment,
    };

    public static readonly PayKind Supplier = new()
    {
        Collection = "supplier-payments", Table = "supplier_payments", PartyField = "supplier_id",
        CashField = "payment_account_id", PartyTable = "suppliers", ControlColumn = "ap_account_id", Sales = false,
        Settler = SettlerKind.SupplierPayment,
    };
}

/// <summary>
/// Customer and supplier payments: drafts that post to the ledger (Dr cash,
/// Cr A/R; or Dr A/P, Cr cash), are applied to invoices or bills, and are
/// unposted by an administrator, which removes their applications too.
/// </summary>
public sealed class Payments<TPay>(TadmorDb db, Journal journal, Settlement settlement, PayKind kind) where TPay : Payment, new()
{
    public PayKind Kind => kind;

    private static readonly HashSet<string> Methods = ["cash", "check", "card", "transfer", "other"];

    public async Task<List<Dictionary<string, object?>>> ListAsync()
    {
        var rows = await db.Set<TPay>().AsNoTracking().OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id).ToListAsync();
        var applied = await AppliedAsync(null);
        return rows.Select(p => Shape(p, applied.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<Dictionary<string, object?>> GetAsync(int id)
    {
        var p = await db.Set<TPay>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id) ?? throw ServiceException.NotFound();
        return Shape(p, (await AppliedAsync(id)).GetValueOrDefault(id));
    }

    public async Task<TPay> FindAsync(int id) =>
        await db.Set<TPay>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id) ?? throw ServiceException.NotFound();

    private async Task<Dictionary<int, decimal>> AppliedAsync(int? id)
    {
        var k = kind.Settler;
        var rows = await db.ListAsync<IdAmount>($$"""
            SELECT {{k.ApplicationSettler}} AS "Id", sum(amount_applied) AS "Amount" FROM {{k.ApplicationTable}}
            WHERE {0}::int IS NULL OR {{k.ApplicationSettler}} = {0}::int GROUP BY 1
            """, id);
        return rows.ToDictionary(r => r.Id, r => r.Amount);
    }

    public sealed class IdAmount
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
    }

    public Dictionary<string, object?> Shape(TPay p, decimal applied) => new()
    {
        ["id"] = p.Id,
        [kind.PartyField] = p.PartyId,
        ["payment_date"] = p.PaymentDate,
        [kind.CashField] = p.CashAccountId,
        ["currency_code"] = p.CurrencyCode,
        ["amount"] = p.Amount,
        ["method"] = p.Method,
        ["reference"] = p.Reference,
        ["status"] = p.Status,
        ["amount_applied"] = applied,
        ["unapplied"] = p.Amount - applied,
        ["journal_entry_id"] = p.JournalEntryId,
    };

    private void Fill(TPay p, Input input)
    {
        p.PartyId = input.ReqId(kind.PartyField);
        p.PaymentDate = input.ReqDate("payment_date");
        p.CurrencyCode = input.ReqStr("currency_code");
        p.Amount = input.ReqDec("amount", Scale.Money);
        p.Method = input.Str("method");
        p.Reference = input.Str("reference");
        p.CashAccountId = input.Int(kind.CashField);
        if (p.Amount <= 0)
        {
            throw ServiceException.Unprocessable("amount must be positive");
        }
        if (p.Method is not null && !Methods.Contains(p.Method))
        {
            throw ServiceException.Unprocessable($"unknown method {p.Method}");
        }
    }

    public async Task<int> CreateAsync(Input input)
    {
        var p = new TPay { CurrencyCode = "" };
        Fill(p, input);
        db.Set<TPay>().Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    public async Task UpdateAsync(int id, Input input)
    {
        var parsed = new TPay { CurrencyCode = "" };
        Fill(parsed, input);
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = await LockAsync(id);
        RequireDraft(p);
        Fill(p, input);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = await LockAsync(id);
        RequireDraft(p);
        db.Set<TPay>().Remove(p);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task<TPay> LockAsync(int id)
    {
        if (await db.ScalarAsync<int?>($"SELECT id FROM {kind.Table} WHERE id = {{0}} FOR UPDATE", id) is null)
        {
            throw ServiceException.NotFound();
        }
        var p = await db.Set<TPay>().SingleAsync(p => p.Id == id);
        await db.Entry(p).ReloadAsync();
        return p;
    }

    private static void RequireDraft(TPay p)
    {
        if (p.Status != "draft")
        {
            throw ServiceException.Conflict($"payment {p.Id} is {p.Status}, not a draft");
        }
    }

    /// <summary>
    /// Posts the payment: both lines at round(amount × rate, 4). It needs
    /// the party's control account and the cash account (422), an open period,
    /// and an exchange rate.
    /// </summary>
    public async Task<int> PostAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = await LockAsync(id);
        RequireDraft(p);
        if (p.Amount <= 0)
        {
            throw ServiceException.Unprocessable("the payment amount must be positive");
        }
        var control = await db.ScalarAsync<int?>($"SELECT {kind.ControlColumn} FROM {kind.PartyTable} WHERE id = {{0}}", p.PartyId)
            ?? throw ServiceException.Unprocessable(kind.Sales ? "the customer has no A/R account" : "the supplier has no A/P account");
        var cash = p.CashAccountId
            ?? throw ServiceException.Unprocessable($"the payment has no {kind.CashField.Replace("_id", "").Replace('_', ' ')}");
        var period = await journal.PeriodForAsync(p.PaymentDate);
        var entry = await journal.CreateEntryAsync(p.PaymentDate, period, p.CurrencyCode, $"Payment {p.Id}", null);
        var cashAmount = kind.Sales ? p.Amount : -p.Amount;
        await journal.AddLineAsync(entry, cash, cashAmount, "Payment");
        await journal.AddLineAsync(entry, control, -cashAmount, "Control");
        p.Status = "posted";
        p.JournalEntryId = entry;
        p.PeriodId = period;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return entry;
    }

    /// <summary>
    /// Unposts the payment (administrators only): its applications are
    /// deleted and the FX entries they posted reversed, then its own entry
    /// is reversed and it returns to draft (spec/domain.md §4.4).
    /// </summary>
    public async Task<int> UnpostAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = await LockAsync(id);
        if (p.Status != "posted" || p.JournalEntryId is not { } entry)
        {
            throw ServiceException.Conflict($"payment {p.Id} is {p.Status}, not posted");
        }
        if (await journal.IsMatchedAsync(entry))
        {
            throw ServiceException.Conflict($"payment {p.Id}'s entry is matched on a bank statement");
        }
        await settlement.RemoveApplicationsAsync(kind.Settler, id);
        var reversal = await journal.ReverseAsync(entry);
        p.Status = "draft";
        p.JournalEntryId = null;
        p.PeriodId = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return reversal;
    }
}
