using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>
/// The year-end close and its reopening (spec/domain.md §9.3),
/// administrators only.
/// </summary>
public sealed class YearEnd(TadmorDb db, Journal journal)
{
    public sealed class AccountBalance
    {
        public int AccountId { get; set; }
        public decimal Balance { get; set; }
    }

    /// <summary>
    /// Closes an open year: posts a closing entry sweeping every revenue and
    /// expense balance up to the year end into retained earnings (unless
    /// there is nothing to sweep), closes every period and then the year,
    /// and rolls forward into a new year when none covers the next day.
    /// Returns the closing entry's id and the new year's, either null.
    /// </summary>
    public async Task<(int? ClosingEntryId, int? NextFiscalYearId)> CloseAsync(int id, Input input)
    {
        var retained = input.ReqId("retained_earnings_account_id");
        await using var tx = await db.Database.BeginTransactionAsync();
        var year = await LockAsync(id);
        if (year.Status != "open")
        {
            throw ServiceException.Conflict($"fiscal year {year.Name} is {year.Status}, not open");
        }
        if (await db.FiscalYears.AnyAsync(y => y.StartDate < year.StartDate && y.Status == "open"))
        {
            throw ServiceException.Unprocessable("an earlier fiscal year is still open; close it first");
        }
        if (!await db.Accounts.AnyAsync(a => a.Id == retained && a.IsPostable && a.IsActive && a.AccountType == "equity"))
        {
            throw ServiceException.Unprocessable("retained earnings must be a postable, active equity account");
        }

        var balances = await db.ListAsync<AccountBalance>("""
            SELECT a.id AS "AccountId", sum(l.base_debit - l.base_credit) AS "Balance"
            FROM journal_lines l
            JOIN journal_entries e ON e.id = l.journal_entry_id
            JOIN accounts a ON a.id = l.account_id
            WHERE e.status = 'posted' AND e.entry_date <= {0} AND a.account_type IN ('revenue', 'expense')
            GROUP BY a.id, a.code HAVING sum(l.base_debit - l.base_credit) <> 0
            ORDER BY a.code, a.id
            """, year.EndDate);

        int? closing = null;
        if (balances.Count > 0)
        {
            var period = await ClosingPeriodAsync(year);
            closing = await journal.CreateEntryAsync(year.EndDate, period, await journal.BaseCurrencyAsync(),
                $"Year-end close of {year.Name}", null, isClosing: true);
            foreach (var b in balances)
            {
                await journal.AddLineAsync(closing.Value, b.AccountId, -b.Balance, "Close to retained earnings");
            }
            var net = balances.Sum(b => b.Balance);
            if (net != 0)
            {
                await journal.AddLineAsync(closing.Value, retained, net, "Net income for the year");
            }
        }

        await db.AccountingPeriods.Where(p => p.FiscalYearId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "closed"));
        year.Status = "closed";
        year.ClosingEntryId = closing;
        await db.SaveChangesAsync();

        int? next = null;
        var start = year.EndDate.AddDays(1);
        if (!await db.FiscalYears.AnyAsync(y => y.StartDate <= start && y.EndDate >= start))
        {
            var end = start.AddYears(1).AddDays(-1);
            var name = $"FY{end.Year}";
            if (!await db.FiscalYears.AnyAsync(y => y.Name == name))
            {
                var created = new FiscalYear { Name = name, StartDate = start, EndDate = end };
                db.FiscalYears.Add(created);
                await db.SaveChangesAsync();
                next = created.Id;
            }
        }
        await tx.CommitAsync();
        return (closing, next);
    }

    /// <summary>
    /// The period the closing entry lands in: the one covering the end date,
    /// created if missing (the month, clipped to the year) and opened if
    /// closed, since every period of the year is closed straight after.
    /// </summary>
    private async Task<int> ClosingPeriodAsync(FiscalYear year)
    {
        var date = year.EndDate;
        var period = await db.AccountingPeriods.Where(p => p.StartDate <= date && p.EndDate >= date).FirstOrDefaultAsync();
        if (period is null)
        {
            var monthStart = new DateOnly(date.Year, date.Month, 1);
            period = new AccountingPeriod
            {
                FiscalYearId = year.Id, Name = date.ToString("yyyy-MM"),
                StartDate = monthStart < year.StartDate ? year.StartDate : monthStart, EndDate = date,
            };
            db.AccountingPeriods.Add(period);
        }
        period.Status = "open";
        await db.SaveChangesAsync();
        return period.Id;
    }

    /// <summary>
    /// Reopens a closed year with no later closed year: the year is opened,
    /// the period holding the closing entry reopened, and the closing entry
    /// reversed (the reversal is flagged as closing too). Other periods stay
    /// closed. Returns the reversal's id, or null if the close posted nothing.
    /// </summary>
    public async Task<int?> ReopenAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var year = await LockAsync(id);
        if (year.Status != "closed")
        {
            throw ServiceException.Conflict($"fiscal year {year.Name} is {year.Status}, not closed");
        }
        if (await db.FiscalYears.AnyAsync(y => y.StartDate > year.StartDate && y.Status == "closed"))
        {
            throw ServiceException.Unprocessable("a later fiscal year is closed; reopen it first");
        }
        year.Status = "open";
        await db.SaveChangesAsync();

        int? reversal = null;
        if (year.ClosingEntryId is { } closing)
        {
            var periodId = await db.JournalEntries.Where(e => e.Id == closing).Select(e => e.PeriodId).SingleAsync();
            await db.AccountingPeriods.Where(p => p.Id == periodId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "open"));
            reversal = await journal.ReverseAsync(closing);
            year.ClosingEntryId = null;
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return reversal;
    }

    private async Task<FiscalYear> LockAsync(int id)
    {
        if (await db.ScalarAsync<int?>("SELECT id FROM fiscal_years WHERE id = {0} FOR UPDATE", id) is null)
        {
            throw ServiceException.NotFound();
        }
        var y = await db.FiscalYears.SingleAsync(y => y.Id == id);
        await db.Entry(y).ReloadAsync();
        return y;
    }
}
