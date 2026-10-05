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

    public sealed class ActivityRow
    {
        public int AccountId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string AccountType { get; set; } = "";
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// Revenue and expense accounts with a line in range, closing entries
    /// excluded, in natural sign: revenue credit − debit, expense debit − credit.
    /// </summary>
    public Task<List<ActivityRow>> ProfitAndLossAsync(DateOnly? from, DateOnly? to) => db.ListAsync<ActivityRow>("""
        SELECT a.id AS "AccountId", a.code AS "Code", a.name AS "Name", a.account_type AS "AccountType",
            CASE WHEN a.account_type = 'revenue' THEN sum(l.base_credit - l.base_debit)
                 ELSE sum(l.base_debit - l.base_credit) END AS "Amount"
        FROM journal_lines l JOIN journal_entries e ON e.id = l.journal_entry_id JOIN accounts a ON a.id = l.account_id
        WHERE e.status = 'posted' AND NOT e.is_closing AND a.account_type IN ('revenue', 'expense')
          AND ({0}::date IS NULL OR e.entry_date >= {0}::date) AND ({1}::date IS NULL OR e.entry_date <= {1}::date)
        GROUP BY a.id, a.code, a.name, a.account_type
        ORDER BY a.code, a.id
        """, from, to);

    public sealed record BalanceSheetDto(List<ActivityRow> Rows, decimal CurrentEarnings);

    /// <summary>
    /// Asset, liability, and equity accounts with lines on or before the
    /// date (assets debit-positive, the rest credit-positive), and current
    /// earnings: credit − debit over revenue and expense lines up to the date,
    /// closing entries included.
    /// </summary>
    public async Task<BalanceSheetDto> BalanceSheetAsync(DateOnly? asOf)
    {
        var rows = await db.ListAsync<ActivityRow>("""
            SELECT a.id AS "AccountId", a.code AS "Code", a.name AS "Name", a.account_type AS "AccountType",
                CASE WHEN a.account_type = 'asset' THEN sum(l.base_debit - l.base_credit)
                     ELSE sum(l.base_credit - l.base_debit) END AS "Amount"
            FROM journal_lines l JOIN journal_entries e ON e.id = l.journal_entry_id JOIN accounts a ON a.id = l.account_id
            WHERE e.status = 'posted' AND a.account_type IN ('asset', 'liability', 'equity')
              AND ({0}::date IS NULL OR e.entry_date <= {0}::date)
            GROUP BY a.id, a.code, a.name, a.account_type
            ORDER BY a.code, a.id
            """, asOf);
        var earnings = await db.ScalarAsync<decimal>("""
            SELECT COALESCE(sum(l.base_credit - l.base_debit), 0)
            FROM journal_lines l JOIN journal_entries e ON e.id = l.journal_entry_id JOIN accounts a ON a.id = l.account_id
            WHERE e.status = 'posted' AND a.account_type IN ('revenue', 'expense')
              AND ({0}::date IS NULL OR e.entry_date <= {0}::date)
            """, asOf);
        return new BalanceSheetDto(rows, earnings);
    }

    public sealed class CashFlowRow
    {
        public int AccountId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Activity { get; set; } = "";
        public decimal Amount { get; set; }
    }

    public sealed record CashFlowDto(decimal NetIncome, List<CashFlowRow> Rows, decimal NetCashFlow, decimal OpeningCash,
        decimal ClosingCash);

    /// <summary>
    /// The indirect-method cash-flow statement: net income, then every
    /// non-cash balance-sheet account with lines in range (closing entries
    /// excluded) at credit − debit under its activity, and the cash
    /// accounts' opening balance, movement, and closing balance.
    /// </summary>
    public async Task<CashFlowDto> CashFlowAsync(DateOnly? from, DateOnly? to)
    {
        const string inRange = "({0}::date IS NULL OR e.entry_date >= {0}::date) AND ({1}::date IS NULL OR e.entry_date <= {1}::date)";
        var netIncome = await db.ScalarAsync<decimal>($$"""
            SELECT COALESCE(sum(l.base_credit - l.base_debit), 0)
            FROM journal_lines l JOIN journal_entries e ON e.id = l.journal_entry_id JOIN accounts a ON a.id = l.account_id
            WHERE e.status = 'posted' AND NOT e.is_closing AND a.account_type IN ('revenue', 'expense') AND {{inRange}}
            """, from, to);
        var rows = await db.ListAsync<CashFlowRow>($$"""
            SELECT a.id AS "AccountId", a.code AS "Code", a.name AS "Name", a.cash_flow_activity AS "Activity",
                sum(l.base_credit - l.base_debit) AS "Amount"
            FROM journal_lines l JOIN journal_entries e ON e.id = l.journal_entry_id JOIN accounts a ON a.id = l.account_id
            WHERE e.status = 'posted' AND NOT e.is_closing AND NOT a.is_cash
              AND a.account_type IN ('asset', 'liability', 'equity') AND {{inRange}}
            GROUP BY a.id, a.code, a.name, a.cash_flow_activity
            ORDER BY a.code, a.id
            """, from, to);
        var opening = from is null ? 0 : await db.ScalarAsync<decimal>("""
            SELECT COALESCE(sum(l.base_debit - l.base_credit), 0)
            FROM journal_lines l JOIN journal_entries e ON e.id = l.journal_entry_id JOIN accounts a ON a.id = l.account_id
            WHERE e.status = 'posted' AND a.is_cash AND e.entry_date < {0}
            """, from.Value);
        var movement = await db.ScalarAsync<decimal>($$"""
            SELECT COALESCE(sum(l.base_debit - l.base_credit), 0)
            FROM journal_lines l JOIN journal_entries e ON e.id = l.journal_entry_id JOIN accounts a ON a.id = l.account_id
            WHERE e.status = 'posted' AND a.is_cash AND {{inRange}}
            """, from, to);
        return new CashFlowDto(netIncome, rows, movement, opening, opening + movement);
    }

    public sealed class AgingRow
    {
        public int PartyId { get; set; }
        public string PartyName { get; set; } = "";
        public decimal TotalOutstanding { get; set; }
        public decimal NotYetDue { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("days_1_30")]
        public decimal Days130 { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("days_31_60")]
        public decimal Days3160 { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("days_61_90")]
        public decimal Days6190 { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("days_over_90")]
        public decimal DaysOver90 { get; set; }
    }

    /// <summary>
    /// A/R or A/P aging: per party with a positive outstanding posted
    /// balance, bucketed by due date against today (the UTC date, which is
    /// the session's), by party id.
    /// </summary>
    public Task<List<AgingRow>> AgingAsync(bool receivables)
    {
        var (view, partyTable, party) = receivables ? ("ar_aging", "customers", "customer_id") : ("ap_aging", "suppliers", "supplier_id");
        return db.ListAsync<AgingRow>($$"""
            SELECT g.{{party}} AS "PartyId", o.name AS "PartyName", g.total_outstanding AS "TotalOutstanding",
                COALESCE(g.not_yet_due, 0) AS "NotYetDue", COALESCE(g.days_1_30, 0) AS "Days130",
                COALESCE(g.days_31_60, 0) AS "Days3160", COALESCE(g.days_61_90, 0) AS "Days6190",
                COALESCE(g.days_over_90, 0) AS "DaysOver90"
            FROM {{view}} g JOIN {{partyTable}} p ON p.id = g.{{party}} JOIN organizations o ON o.id = p.organization_id
            ORDER BY g.{{party}}
            """);
    }
}
