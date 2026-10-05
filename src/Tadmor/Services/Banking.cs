using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>
/// Bank reconciliation (spec/api.md §5.13, spec/domain.md §8): statements
/// on a cash account, their lines keyed by hand or imported from CSV, each
/// matched to one posted journal line of the same signed amount, and the
/// statement reconciled once everything is matched and it adds up. The
/// schema enforces the account rule, the match rules, and the freezing of
/// reconciled statements; the checks here come first, to give each refusal
/// the spec's status.
/// </summary>
public sealed partial class Banking(TadmorDb db)
{
    public sealed class StatementRow
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public DateOnly StatementDate { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal ClosingBalance { get; set; }
        public string? Reference { get; set; }
        public string Status { get; set; } = "";
        public int LineCount { get; set; }
        public int MatchedCount { get; set; }
        public decimal LinesTotal { get; set; }
        public decimal Difference { get; set; }
    }

    private const string StatementSql = """
        SELECT s.id AS "Id", s.account_id AS "AccountId", a.code AS "AccountCode", a.name AS "AccountName",
            s.statement_date AS "StatementDate", s.opening_balance AS "OpeningBalance", s.closing_balance AS "ClosingBalance",
            s.reference AS "Reference", s.status AS "Status",
            (SELECT count(*)::int FROM bank_statement_lines WHERE statement_id = s.id) AS "LineCount",
            (SELECT count(*)::int FROM bank_statement_lines WHERE statement_id = s.id AND journal_line_id IS NOT NULL) AS "MatchedCount",
            COALESCE((SELECT sum(amount) FROM bank_statement_lines WHERE statement_id = s.id), 0) AS "LinesTotal",
            s.opening_balance + COALESCE((SELECT sum(amount) FROM bank_statement_lines WHERE statement_id = s.id), 0)
                - s.closing_balance AS "Difference"
        FROM bank_statements s JOIN accounts a ON a.id = s.account_id
        """;

    public Task<List<StatementRow>> ListAsync() =>
        db.ListAsync<StatementRow>(StatementSql + " ORDER BY s.statement_date DESC, s.id DESC");

    public async Task<StatementRow> GetAsync(int id) =>
        (await db.ListAsync<StatementRow>(StatementSql + " WHERE s.id = {0}", id)).SingleOrDefault() ?? throw ServiceException.NotFound();

    private static void Fill(BankStatement s, Input input)
    {
        s.AccountId = input.ReqId("account_id");
        s.StatementDate = input.ReqDate("statement_date");
        s.OpeningBalance = input.ReqDec("opening_balance", Scale.Money);
        s.ClosingBalance = input.ReqDec("closing_balance", Scale.Money);
        s.Reference = input.Str("reference");
    }

    private async Task RequireCashAccountAsync(int accountId)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.IsPostable && a.IsActive && a.IsCash))
        {
            throw ServiceException.Unprocessable($"account {accountId} is not a postable, active cash account");
        }
    }

    public async Task<int> CreateAsync(Input input)
    {
        var s = new BankStatement();
        Fill(s, input);
        await RequireCashAccountAsync(s.AccountId);
        db.BankStatements.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    public async Task UpdateAsync(int id, Input input)
    {
        Fill(new BankStatement(), input);
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = await LockOpenAsync(id);
        var oldAccount = s.AccountId;
        Fill(s, input);
        if (s.AccountId != oldAccount)
        {
            await RequireCashAccountAsync(s.AccountId);
            if (await db.BankStatementLines.AnyAsync(l => l.StatementId == id && l.JournalLineId != null))
            {
                throw ServiceException.Unprocessable("the statement has matched lines; unmatch them before changing its account");
            }
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = await LockOpenAsync(id);
        db.BankStatements.Remove(s);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>The statement, locked; 404 if there is none, 409 unless it is open.</summary>
    private async Task<BankStatement> LockOpenAsync(int id)
    {
        var s = await LockAsync(id);
        if (s.Status != "open")
        {
            throw ServiceException.Conflict($"bank statement {id} is {s.Status}, not open");
        }
        return s;
    }

    private async Task<BankStatement> LockAsync(int id)
    {
        if (await db.ScalarAsync<int?>("SELECT id FROM bank_statements WHERE id = {0} FOR UPDATE", id) is null)
        {
            throw ServiceException.NotFound();
        }
        var s = await db.BankStatements.SingleAsync(s => s.Id == id);
        await db.Entry(s).ReloadAsync();
        return s;
    }

    public sealed class LineRow
    {
        public int Id { get; set; }
        public int LineNo { get; set; }
        public DateOnly TxnDate { get; set; }
        public string Description { get; set; } = "";
        public string? Reference { get; set; }
        public decimal Amount { get; set; }
        public int? JournalLineId { get; set; }
        public int? JournalEntryId { get; set; }
        public DateOnly? EntryDate { get; set; }
        public string? EntryMemo { get; set; }
    }

    public async Task<List<LineRow>> LinesAsync(int id)
    {
        if (!await db.BankStatements.AnyAsync(s => s.Id == id))
        {
            throw ServiceException.NotFound();
        }
        return await db.ListAsync<LineRow>("""
            SELECT b.id AS "Id", b.line_no AS "LineNo", b.txn_date AS "TxnDate", b.description AS "Description",
                b.reference AS "Reference", b.amount AS "Amount", b.journal_line_id AS "JournalLineId",
                e.id AS "JournalEntryId", e.entry_date AS "EntryDate", e.memo AS "EntryMemo"
            FROM bank_statement_lines b
            LEFT JOIN journal_lines jl ON jl.id = b.journal_line_id
            LEFT JOIN journal_entries e ON e.id = jl.journal_entry_id
            WHERE b.statement_id = {0} ORDER BY b.line_no
            """, id);
    }

    private sealed record NewLine(DateOnly Date, string Description, string? Reference, decimal Amount);

    /// <summary>Appends a line by hand; returns its id.</summary>
    public async Task<int> AddLineAsync(int id, Input input)
    {
        var line = new NewLine(input.ReqDate("txn_date"), input.ReqStr("description"), input.Str("reference"),
            input.ReqDec("amount", Scale.Money));
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockOpenAsync(id);
        if (line.Amount == 0)
        {
            throw ServiceException.Unprocessable("amount must not be zero");
        }
        var ids = await AppendAsync(id, [line]);
        await tx.CommitAsync();
        return ids[0];
    }

    /// <summary>
    /// Imports CSV lines (date,description,amount[,reference]), all or
    /// nothing, appended after any existing ones (spec/domain.md §8.2).
    /// </summary>
    public async Task<int> ImportAsync(int id, Input input)
    {
        var csv = input.Str("csv") ?? throw ServiceException.BadRequest("csv is required");
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockOpenAsync(id);
        var lines = ParseCsv(csv);
        await AppendAsync(id, lines);
        await tx.CommitAsync();
        return lines.Count;
    }

    private async Task<List<int>> AppendAsync(int statementId, List<NewLine> lines)
    {
        var next = await db.BankStatementLines.Where(l => l.StatementId == statementId).MaxAsync(l => (int?)l.LineNo) ?? 0;
        var created = new List<BankStatementLine>();
        foreach (var l in lines)
        {
            var row = new BankStatementLine
            {
                StatementId = statementId, LineNo = ++next, TxnDate = l.Date, Description = l.Description,
                Reference = l.Reference, Amount = l.Amount,
            };
            db.BankStatementLines.Add(row);
            created.Add(row);
        }
        await db.SaveChangesAsync();
        return created.Select(r => r.Id).ToList();
    }

    private static List<NewLine> ParseCsv(string csv)
    {
        var records = SplitCsv(csv).Where(r => r.Any(f => f.Length > 0)).ToList();
        if (records.Count > 0 && Input.TryParseDate(records[0][0]) is null)
        {
            records.RemoveAt(0);
        }
        if (records.Count == 0)
        {
            throw ServiceException.Unprocessable("the CSV has no data rows");
        }
        var lines = new List<NewLine>();
        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            string Bad(string why) => $"CSV row {i + 1}: {why}";
            if (r.Count is not (3 or 4))
            {
                throw ServiceException.Unprocessable(Bad("want date,description,amount[,reference]"));
            }
            var date = Input.TryParseDate(r[0]) ?? throw ServiceException.Unprocessable(Bad($"invalid date {r[0]}"));
            if (r[1].Length == 0)
            {
                throw ServiceException.Unprocessable(Bad("the description is empty"));
            }
            if (!CsvAmount().IsMatch(r[2]))
            {
                throw ServiceException.Unprocessable(Bad($"invalid amount {r[2]}"));
            }
            var amount = Input.ParseDecimal(r[2], Scale.Money, "amount");
            if (amount == 0)
            {
                throw ServiceException.Unprocessable(Bad("the amount is zero"));
            }
            lines.Add(new NewLine(date, r[1], r.Count == 4 && r[3].Length > 0 ? r[3] : null, amount));
        }
        return lines;
    }

    /// <summary>RFC 4180 records with quoted fields, every field trimmed.</summary>
    private static List<List<string>> SplitCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                record.Add(field.ToString().Trim());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                record.Add(field.ToString().Trim());
                field.Clear();
                records.Add(record);
                record = [];
            }
            else
            {
                field.Append(c);
            }
        }
        record.Add(field.ToString().Trim());
        records.Add(record);
        return records;
    }

    [GeneratedRegex(@"^-?(\d+(\.\d*)?|\.\d+)$")]
    private static partial Regex CsvAmount();

    public sealed class Candidate
    {
        public int JournalLineId { get; set; }
        public int JournalEntryId { get; set; }
        public DateOnly EntryDate { get; set; }
        public string? Reference { get; set; }
        public string? Memo { get; set; }
        public decimal Amount { get; set; }
    }

    private Task<List<Candidate>> CandidatesForAsync(int accountId) => db.ListAsync<Candidate>("""
        SELECT jl.id AS "JournalLineId", e.id AS "JournalEntryId", e.entry_date AS "EntryDate", e.reference AS "Reference",
            COALESCE(jl.memo, e.memo) AS "Memo", jl.debit - jl.credit AS "Amount"
        FROM journal_lines jl JOIN journal_entries e ON e.id = jl.journal_entry_id
        WHERE jl.account_id = {0} AND e.status = 'posted'
          AND NOT EXISTS (SELECT 1 FROM bank_statement_lines b WHERE b.journal_line_id = jl.id)
        ORDER BY e.entry_date, e.id, jl.line_no, jl.id
        """, accountId);

    /// <summary>The account's posted journal lines that no statement line has claimed.</summary>
    public async Task<List<Candidate>> CandidatesAsync(int id)
    {
        var s = await db.BankStatements.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id) ?? throw ServiceException.NotFound();
        return await CandidatesForAsync(s.AccountId);
    }

    /// <summary>
    /// Matches each unmatched line, in line order, to the unclaimed candidate
    /// with its amount and the nearest entry date (ties to the lowest journal
    /// line id); returns how many were matched.
    /// </summary>
    public async Task<int> AutoMatchAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = await LockOpenAsync(id);
        var candidates = await CandidatesForAsync(s.AccountId);
        var lines = await db.BankStatementLines.Where(l => l.StatementId == id && l.JournalLineId == null)
            .OrderBy(l => l.LineNo).ToListAsync();
        var matched = 0;
        foreach (var line in lines)
        {
            var best = candidates.Where(c => c.Amount == line.Amount)
                .OrderBy(c => Math.Abs(c.EntryDate.DayNumber - line.TxnDate.DayNumber)).ThenBy(c => c.JournalLineId)
                .FirstOrDefault();
            if (best is null)
            {
                continue;
            }
            line.JournalLineId = best.JournalLineId;
            candidates.Remove(best);
            matched++;
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return matched;
    }

    /// <summary>Requires every line matched and opening + lines = closing (422).</summary>
    public async Task ReconcileAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = await LockOpenAsync(id);
        if (await db.BankStatementLines.AnyAsync(l => l.StatementId == id && l.JournalLineId == null))
        {
            throw ServiceException.Unprocessable("every line must be matched before the statement is reconciled");
        }
        var total = await db.BankStatementLines.Where(l => l.StatementId == id).SumAsync(l => (decimal?)l.Amount) ?? 0;
        if (s.OpeningBalance + total != s.ClosingBalance)
        {
            throw ServiceException.Unprocessable("the statement does not balance: opening + lines ≠ closing");
        }
        s.Status = "reconciled";
        s.ReconciledAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Reconciled back to open (administrators only).</summary>
    public async Task ReopenAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = await LockAsync(id);
        if (s.Status != "reconciled")
        {
            throw ServiceException.Conflict($"bank statement {id} is {s.Status}, not reconciled");
        }
        s.Status = "open";
        s.ReconciledAt = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>The line and its statement, locked; 404, or 409 unless the statement is open.</summary>
    private async Task<(BankStatementLine Line, BankStatement Statement)> LockLineAsync(int lineId)
    {
        var statementId = await db.BankStatementLines.Where(l => l.Id == lineId).Select(l => (int?)l.StatementId).SingleOrDefaultAsync()
            ?? throw ServiceException.NotFound();
        var s = await LockOpenAsync(statementId);
        var line = await db.BankStatementLines.SingleAsync(l => l.Id == lineId);
        await db.Entry(line).ReloadAsync();
        return (line, s);
    }

    /// <summary>
    /// Matches a line to a journal line: refused (409) if the statement is
    /// not open, the line is matched, or the journal line backs another
    /// statement line, and (422) if the journal line does not exist, is not
    /// posted, is on another account, or has a different signed amount.
    /// </summary>
    public async Task MatchAsync(int lineId, Input input)
    {
        var journalLine = input.ReqId("journal_line_id");
        await using var tx = await db.Database.BeginTransactionAsync();
        var (line, s) = await LockLineAsync(lineId);
        if (line.JournalLineId is not null)
        {
            throw ServiceException.Conflict($"statement line {lineId} is already matched");
        }
        if (await db.BankStatementLines.AnyAsync(l => l.JournalLineId == journalLine))
        {
            throw ServiceException.Conflict($"journal line {journalLine} already backs a statement line");
        }
        var jl = await (from l in db.JournalLines
                        join e in db.JournalEntries on l.JournalEntryId equals e.Id
                        where l.Id == journalLine
                        select new { l.AccountId, Amount = l.Debit - l.Credit, e.Status }).SingleOrDefaultAsync()
            ?? throw ServiceException.Unprocessable($"journal line {journalLine} does not exist");
        if (jl.Status != "posted")
        {
            throw ServiceException.Unprocessable($"journal line {journalLine} is not posted");
        }
        if (jl.AccountId != s.AccountId)
        {
            throw ServiceException.Unprocessable($"journal line {journalLine} is not on the statement's account");
        }
        if (jl.Amount != line.Amount)
        {
            throw ServiceException.Unprocessable($"journal line {journalLine} is for {jl.Amount}, the statement line for {line.Amount}");
        }
        line.JournalLineId = journalLine;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Unmatches a line; a no-op if it is unmatched.</summary>
    public async Task UnmatchAsync(int lineId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var (line, _) = await LockLineAsync(lineId);
        line.JournalLineId = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task DeleteLineAsync(int lineId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var (line, _) = await LockLineAsync(lineId);
        db.BankStatementLines.Remove(line);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
