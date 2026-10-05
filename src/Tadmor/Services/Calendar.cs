using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>
/// Fiscal years and accounting periods (spec/api.md §5.7, spec/domain.md
/// §9.1). Year-end close and reopen post entries and live in YearEnd.
/// The schema enforces the calendar's rules: unique year names and period
/// names within a year (409), end not before start, periods that overlap no
/// other period in any year, and no open period in a closed year (422).
/// </summary>
public sealed class Calendar(TadmorDb db)
{
    public sealed record FiscalYearDto(int Id, string Name, DateOnly StartDate, DateOnly EndDate, string Status);

    public sealed record PeriodDto(int Id, int FiscalYearId, string Name, DateOnly StartDate, DateOnly EndDate, string Status);

    private static readonly Expression<Func<FiscalYear, FiscalYearDto>> YearRows =
        y => new FiscalYearDto(y.Id, y.Name, y.StartDate, y.EndDate, y.Status);

    public Task<List<FiscalYearDto>> FiscalYearsAsync() => db.FiscalYears.OrderBy(y => y.StartDate).ThenBy(y => y.Id).Select(YearRows).ToListAsync();

    public async Task<FiscalYearDto> FiscalYearAsync(int id) =>
        await db.FiscalYears.Where(y => y.Id == id).Select(YearRows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    /// <summary>Creates the year, open.</summary>
    public async Task<int> CreateFiscalYearAsync(Input input)
    {
        var y = new FiscalYear { Name = input.ReqStr("name"), StartDate = input.ReqDate("start_date"), EndDate = input.ReqDate("end_date") };
        db.FiscalYears.Add(y);
        await db.SaveChangesAsync();
        return y.Id;
    }

    /// <summary>Status is not part of the body: open and closed belong to close and reopen.</summary>
    public async Task UpdateFiscalYearAsync(int id, Input input)
    {
        var name = input.ReqStr("name");
        var start = input.ReqDate("start_date");
        var end = input.ReqDate("end_date");
        var y = await db.FiscalYears.FindAsync(id) ?? throw ServiceException.NotFound();
        y.Name = name;
        y.StartDate = start;
        y.EndDate = end;
        await db.SaveChangesAsync();
    }

    private static readonly Expression<Func<AccountingPeriod, PeriodDto>> PeriodRows =
        p => new PeriodDto(p.Id, p.FiscalYearId, p.Name, p.StartDate, p.EndDate, p.Status);

    public Task<List<PeriodDto>> PeriodsAsync() => db.AccountingPeriods.OrderBy(p => p.StartDate).ThenBy(p => p.Id).Select(PeriodRows).ToListAsync();

    public async Task<PeriodDto> PeriodAsync(int id) =>
        await db.AccountingPeriods.Where(p => p.Id == id).Select(PeriodRows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    /// <summary>Creates the period, open, in an open fiscal year.</summary>
    public async Task<int> CreatePeriodAsync(Input input)
    {
        var p = new AccountingPeriod
        {
            FiscalYearId = input.ReqId("fiscal_year_id"), Name = input.ReqStr("name"),
            StartDate = input.ReqDate("start_date"), EndDate = input.ReqDate("end_date"),
        };
        var year = await db.FiscalYears.FindAsync(p.FiscalYearId)
            ?? throw ServiceException.Unprocessable($"fiscal year {p.FiscalYearId} does not exist");
        if (year.Status != "open")
        {
            throw ServiceException.Unprocessable($"fiscal year {year.Name} is closed");
        }
        db.AccountingPeriods.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    /// <summary>Editing a period is also how it is closed or reopened; status defaults to open.</summary>
    public async Task UpdatePeriodAsync(int id, Input input)
    {
        var yearId = input.ReqId("fiscal_year_id");
        var name = input.ReqStr("name");
        var start = input.ReqDate("start_date");
        var end = input.ReqDate("end_date");
        var status = input.Str("status") ?? "open";
        var p = await db.AccountingPeriods.FindAsync(id) ?? throw ServiceException.NotFound();
        p.FiscalYearId = yearId;
        p.Name = name;
        p.StartDate = start;
        p.EndDate = end;
        p.Status = status;
        await db.SaveChangesAsync();
    }
}

/// <summary>Ledger settings and exchange rates (spec/api.md §5.8, spec/domain.md §7.1).</summary>
public sealed class Ledger(TadmorDb db)
{
    public sealed record SettingsDto(string BaseCurrency, int? FxGainLossAccountId);

    public sealed record RateDto(string CurrencyCode, DateOnly RateDate, decimal Rate);

    public async Task<SettingsDto> SettingsAsync()
    {
        var s = await db.GlSettings.AsNoTracking().SingleAsync();
        return new SettingsDto(s.BaseCurrency, s.FxGainLossAccountId);
    }

    /// <summary>
    /// The base currency is upper-cased; it cannot change once any journal
    /// entry exists (the schema refuses). The FX account must be postable
    /// and active.
    /// </summary>
    public async Task UpdateSettingsAsync(Input input)
    {
        var currency = input.ReqStr("base_currency").ToUpperInvariant();
        var fx = input.Int("fx_gain_loss_account_id");
        if (!await db.Currencies.AnyAsync(c => c.Code == currency))
        {
            throw ServiceException.Unprocessable($"unknown currency {currency}");
        }
        if (fx is { } fxId && !await db.Accounts.AnyAsync(a => a.Id == fxId && a.IsPostable && a.IsActive))
        {
            throw ServiceException.Unprocessable("the FX gain/loss account must be a postable, active account");
        }
        var s = await db.GlSettings.SingleAsync();
        s.BaseCurrency = currency;
        s.FxGainLossAccountId = fx;
        await db.SaveChangesAsync();
    }

    public async Task<List<RateDto>> RatesAsync() => (await db.ExchangeRates
        .OrderBy(r => r.CurrencyCode).ThenByDescending(r => r.RateDate).ToListAsync())
        .Select(r => new RateDto(r.CurrencyCode, r.RateDate, Decimals.Trim(r.Rate))).ToList();

    /// <summary>Creates a rate; returns its key with the currency upper-cased as stored.</summary>
    public async Task<(string Currency, DateOnly Date)> CreateRateAsync(Input input)
    {
        var currency = input.ReqStr("currency_code").ToUpperInvariant();
        var date = input.ReqDate("rate_date");
        var rate = input.ReqDec("rate", Scale.Fx);
        if (rate <= 0)
        {
            throw ServiceException.Unprocessable("rate must be positive");
        }
        db.ExchangeRates.Add(new ExchangeRate { CurrencyCode = currency, RateDate = date, Rate = rate });
        await db.SaveChangesAsync();
        return (currency, date);
    }

    public async Task UpdateRateAsync(string currency, DateOnly date, Input input)
    {
        var rate = input.ReqDec("rate", Scale.Fx);
        var r = await db.ExchangeRates.FindAsync(currency.ToUpperInvariant(), date) ?? throw ServiceException.NotFound();
        if (rate <= 0)
        {
            throw ServiceException.Unprocessable("rate must be positive");
        }
        r.Rate = rate;
        await db.SaveChangesAsync();
    }

    public async Task DeleteRateAsync(string currency, DateOnly date)
    {
        var code = currency.ToUpperInvariant();
        if (await db.ExchangeRates.Where(r => r.CurrencyCode == code && r.RateDate == date).ExecuteDeleteAsync() == 0)
        {
            throw ServiceException.NotFound();
        }
    }
}
