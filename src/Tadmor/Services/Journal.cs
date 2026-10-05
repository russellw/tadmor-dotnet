using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>
/// The journal machinery every posting shares: finding the accounting
/// period for a date (spec/domain.md §9.2), opening an entry at the right
/// exchange rate (§7.1), and reversing an entry (§4.4). Callers run inside
/// their own transaction, so a refusal part way leaves nothing behind, not
/// even a period created for the attempt.
///
/// The schema checks every entry at commit: it must balance in both its own
/// and the base currency, touch only postable, active accounts, and lie in
/// an open period. Amounts in another currency are converted in SQL, where
/// numeric arithmetic is exact, and only then rounded.
/// </summary>
public sealed class Journal(TadmorDb db)
{
    /// <summary>
    /// The open period covering the date. If none covers it but an open
    /// fiscal year does, that calendar month's period is created, named
    /// YYYY-MM and clipped to the year. A closed period covering the date, no
    /// fiscal year, or a new period that would overlap another, is a 422.
    /// </summary>
    public async Task<int> PeriodForAsync(DateOnly date)
    {
        if (await OpenPeriodAsync(date) is { } open)
        {
            return open;
        }
        if (await db.AccountingPeriods.AnyAsync(p => p.StartDate <= date && p.EndDate >= date))
        {
            throw NoOpenPeriod(date);
        }
        // ON CONFLICT DO NOTHING also absorbs the no-overlap exclusion constraint.
        await db.ExecAsync("""
            INSERT INTO accounting_periods (fiscal_year_id, name, start_date, end_date)
            SELECT fy.id, to_char({0}::date, 'YYYY-MM'),
                GREATEST(date_trunc('month', {0}::date)::date, fy.start_date),
                LEAST((date_trunc('month', {0}::date) + interval '1 month - 1 day')::date, fy.end_date)
            FROM fiscal_years fy
            WHERE {0}::date BETWEEN fy.start_date AND fy.end_date AND fy.status = 'open'
            ORDER BY fy.start_date LIMIT 1
            ON CONFLICT DO NOTHING
            """, date);
        return await OpenPeriodAsync(date) ?? throw NoOpenPeriod(date);
    }

    private async Task<int?> OpenPeriodAsync(DateOnly date) =>
        await db.AccountingPeriods.Where(p => p.StartDate <= date && p.EndDate >= date && p.Status == "open")
            .OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync();

    private static ServiceException NoOpenPeriod(DateOnly date) =>
        ServiceException.Unprocessable($"no open accounting period covers {date:yyyy-MM-dd}");

    /// <summary>
    /// Opens a posted entry, without lines, and returns its id. The rate is 1
    /// for the base currency, else the currency's latest rate dated on or
    /// before the entry (422 if there is none); the entry keeps it, so later
    /// rate edits never change history.
    /// </summary>
    public async Task<int> CreateEntryAsync(DateOnly date, int periodId, string currency, string? memo, string? reference,
        bool isClosing = false)
    {
        var id = await db.ScalarAsync<int?>("""
            INSERT INTO journal_entries (entry_date, period_id, currency_code, exchange_rate, memo, reference,
                status, posted_at, is_closing)
            SELECT {0}, {1}, {2}, r.rate, {3}, {4}, 'posted', now(), {5}
            FROM (SELECT CASE WHEN {2}::text = (SELECT base_currency FROM gl_settings) THEN 1::numeric
                              ELSE (SELECT rate FROM exchange_rates
                                    WHERE currency_code = {2}::text AND rate_date <= {0}
                                    ORDER BY rate_date DESC LIMIT 1)
                         END AS rate) r
            WHERE r.rate IS NOT NULL
            RETURNING id
            """, date, periodId, currency, memo, reference, isClosing);
        return id ?? throw ServiceException.Unprocessable(
            $"no exchange rate for {currency} on or before {date:yyyy-MM-dd}");
    }

    /// <summary>The base currency, which stock, closing, and FX entries use.</summary>
    public async Task<string> BaseCurrencyAsync() => await db.GlSettings.Select(s => s.BaseCurrency).SingleAsync();

    /// <summary>
    /// Adds a line to an entry. The amount is signed: positive is a debit,
    /// negative a credit. The base amount is round(|amount| × the entry's
    /// rate, 4), unless given.
    /// </summary>
    public Task AddLineAsync(int entryId, int accountId, decimal amount, string? memo, decimal? baseAmount = null) =>
        db.ExecAsync("""
            INSERT INTO journal_lines (journal_entry_id, line_no, account_id, debit, credit, memo, base_debit, base_credit)
            SELECT e.id, (SELECT count(*) + 1 FROM journal_lines WHERE journal_entry_id = e.id), {1},
                greatest({2}::numeric, 0), greatest(-{2}::numeric, 0), {3},
                CASE WHEN {2}::numeric > 0 THEN COALESCE({4}::numeric, round({2}::numeric * e.exchange_rate, 4)) ELSE 0 END,
                CASE WHEN {2}::numeric < 0 THEN COALESCE({4}::numeric, round(-{2}::numeric * e.exchange_rate, 4)) ELSE 0 END
            FROM journal_entries e WHERE e.id = {0}
            """, entryId, accountId, amount, memo, baseAmount);

    /// <summary>
    /// Posts the mirror of an entry: same date, currency, rate, and closing
    /// flag, every line's sides swapped, linked to the original, which stays
    /// posted. Refused (409) if the entry was already reversed or any of its
    /// lines is matched on a bank statement, and (422) if no open period
    /// covers its date.
    /// </summary>
    public async Task<int> ReverseAsync(int entryId)
    {
        var date = await db.JournalEntries.Where(e => e.Id == entryId).Select(e => (DateOnly?)e.EntryDate).SingleOrDefaultAsync()
            ?? throw ServiceException.NotFound();
        if (await db.JournalEntries.AnyAsync(e => e.ReversesEntryId == entryId))
        {
            throw ServiceException.Conflict($"journal entry {entryId} is already reversed");
        }
        if (await IsMatchedAsync(entryId))
        {
            throw ServiceException.Conflict($"journal entry {entryId} has lines matched on a bank statement");
        }
        var period = await PeriodForAsync(date);
        var reversal = await db.ScalarAsync<int>("""
            INSERT INTO journal_entries (entry_date, period_id, currency_code, exchange_rate, memo,
                reverses_entry_id, status, posted_at, is_closing)
            SELECT entry_date, {0}, currency_code, exchange_rate, 'Reversal of journal entry ' || id,
                id, 'posted', now(), is_closing
            FROM journal_entries WHERE id = {1}
            RETURNING id
            """, period, entryId);
        await db.ExecAsync("""
            INSERT INTO journal_lines (journal_entry_id, line_no, account_id, debit, credit, memo, base_debit, base_credit)
            SELECT {0}, line_no, account_id, credit, debit, memo, base_credit, base_debit
            FROM journal_lines WHERE journal_entry_id = {1}
            """, reversal, entryId);
        return reversal;
    }

    /// <summary>Whether any line of the entry is matched on a bank statement.</summary>
    public Task<bool> IsMatchedAsync(int entryId) =>
        (from b in db.BankStatementLines
         join l in db.JournalLines on b.JournalLineId equals l.Id
         where l.JournalEntryId == entryId
         select b).AnyAsync();
}

/// <summary>Raw SQL over the context's connection and transaction, with {0}-style parameters.</summary>
public static class Sql
{
    public static Task<int> ExecAsync(this TadmorDb db, string sql, params object?[] args) =>
        db.Database.ExecuteSqlRawAsync(sql, Parameters(args));

    public static async Task<T> ScalarAsync<T>(this TadmorDb db, string sql, params object?[] args)
    {
        var rows = await db.Database.SqlQueryRaw<T>(sql, Parameters(args)).ToListAsync();
        return rows.Count == 0 ? default! : rows[0];
    }

    public static Task<List<T>> ListAsync<T>(this TadmorDb db, string sql, params object?[] args) =>
        db.Database.SqlQueryRaw<T>(sql, Parameters(args)).ToListAsync();

    // A null goes as an untyped NULL parameter, whose type Postgres infers
    // from where it is used; the SQL casts where that is ambiguous.
    private static object[] Parameters(object?[] args) =>
        args.Select(a => a ?? new Npgsql.NpgsqlParameter { Value = DBNull.Value }).ToArray();
}
