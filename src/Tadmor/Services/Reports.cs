using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>
/// The journal and the reports (spec/api.md §5.14, spec/domain.md §10).
/// Every figure covers posted entries only and is in the base currency,
/// summed in SQL from the journal lines.
/// </summary>
public sealed class Reports(TadmorDb db)
{
    public sealed class EntryLine
    {
        public int LineNo { get; set; }
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string? Memo { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal BaseDebit { get; set; }
        public decimal BaseCredit { get; set; }
    }

    public sealed record EntryDto(int Id, DateOnly EntryDate, string CurrencyCode, decimal ExchangeRate, string? Reference,
        string? Memo, string Status, bool IsClosing, int? ReversesEntryId, List<EntryLine> Lines);

    public async Task<EntryDto> EntryAsync(int id)
    {
        var e = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id) ?? throw ServiceException.NotFound();
        var lines = await db.ListAsync<EntryLine>("""
            SELECT l.line_no AS "LineNo", l.account_id AS "AccountId", a.code AS "AccountCode", a.name AS "AccountName",
                l.memo AS "Memo", l.debit AS "Debit", l.credit AS "Credit", l.base_debit AS "BaseDebit", l.base_credit AS "BaseCredit"
            FROM journal_lines l JOIN accounts a ON a.id = l.account_id
            WHERE l.journal_entry_id = {0} ORDER BY l.line_no
            """, id);
        return new EntryDto(e.Id, e.EntryDate, e.CurrencyCode, Decimals.Trim(e.ExchangeRate), e.Reference, e.Memo, e.Status,
            e.IsClosing, e.ReversesEntryId, lines);
    }

    public sealed class LedgerRow
    {
        public int JournalEntryId { get; set; }
        public DateOnly EntryDate { get; set; }
        public string? Reference { get; set; }
        public string? Memo { get; set; }
        public string CurrencyCode { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal BaseDebit { get; set; }
        public decimal BaseCredit { get; set; }
    }

    /// <summary>
    /// An account's posted lines with inclusive optional bounds, by entry
    /// date, entry, then line; each line's memo falls back to its entry's.
    /// </summary>
    public async Task<List<LedgerRow>> LedgerAsync(int accountId, DateOnly? from, DateOnly? to)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId))
        {
            throw ServiceException.NotFound();
        }
        return await db.ListAsync<LedgerRow>("""
            SELECT e.id AS "JournalEntryId", e.entry_date AS "EntryDate", e.reference AS "Reference",
                COALESCE(l.memo, e.memo) AS "Memo", e.currency_code AS "CurrencyCode", l.debit AS "Debit",
                l.credit AS "Credit", l.base_debit AS "BaseDebit", l.base_credit AS "BaseCredit"
            FROM journal_lines l JOIN journal_entries e ON e.id = l.journal_entry_id
            WHERE l.account_id = {0} AND e.status = 'posted'
              AND ({1}::date IS NULL OR e.entry_date >= {1}::date) AND ({2}::date IS NULL OR e.entry_date <= {2}::date)
            ORDER BY e.entry_date, e.id, l.line_no
            """, accountId, from, to);
    }

    public sealed class TrialBalanceRow
    {
        public int AccountId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string AccountType { get; set; } = "";
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Balance { get; set; }
    }

    /// <summary>Every account, with or without activity, by code; the balance is debit-positive.</summary>
    public Task<List<TrialBalanceRow>> TrialBalanceAsync() => db.ListAsync<TrialBalanceRow>("""
        SELECT account_id AS "AccountId", code AS "Code", name AS "Name", account_type AS "AccountType",
            total_debit AS "TotalDebit", total_credit AS "TotalCredit", balance AS "Balance"
        FROM trial_balance ORDER BY code, account_id
        """);
}
