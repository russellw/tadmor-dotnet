using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>
/// What distinguishes the four line documents (spec/api.md §5.9): their
/// tables, the JSON names of their fields, and which side of the ledger
/// they post to. SQL identifiers here are constants, never input.
/// </summary>
public sealed record DocKind
{
    public required string Collection { get; init; }
    /// <summary>The label for messages, PDFs, and email subjects.</summary>
    public required string Label { get; init; }
    public required string Table { get; init; }
    public required string LinesTable { get; init; }
    /// <summary>The lines' column naming their document.</summary>
    public required string LineDocument { get; init; }
    public required string NumberField { get; init; }
    public required string PartyField { get; init; }
    public required string DateField { get; init; }
    public required bool HasDueDate { get; init; }
    public required string PriceField { get; init; }
    public required string AccountField { get; init; }
    /// <summary>The product column supplying a line's account when it names none (spec/domain.md §3).</summary>
    public required string ProductAccount { get; init; }
    public required string PartyTable { get; init; }
    public required string ControlColumn { get; init; }
    public required bool Sales { get; init; }
    public required bool IsCreditNote { get; init; }
    /// <summary>Whether detail lines post on the credit side (invoices, supplier credits).</summary>
    public required bool DetailOnCredit { get; init; }
    /// <summary>payment_status (invoices, bills) or application_status (credit notes).</summary>
    public string SettlementField => IsCreditNote ? "application_status" : "payment_status";

    /// <summary>SQL that is true when anything is applied to or from document {0}.</summary>
    public required string AppliedSql { get; init; }

    public static readonly DocKind SalesInvoice = new()
    {
        Collection = "sales-invoices", Label = "Invoice", Table = "sales_invoices", LinesTable = "sales_invoice_lines",
        LineDocument = "invoice_id", NumberField = "invoice_number", PartyField = "customer_id", DateField = "invoice_date",
        HasDueDate = true, PriceField = "unit_price", AccountField = "revenue_account_id", ProductAccount = "revenue_account_id",
        PartyTable = "customers", ControlColumn = "ar_account_id", Sales = true, IsCreditNote = false, DetailOnCredit = true,
        AppliedSql = "SELECT 1 FROM payment_applications WHERE invoice_id = {0} UNION ALL SELECT 1 FROM sales_credit_applications WHERE invoice_id = {0}",
    };

    public static readonly DocKind PurchaseBill = new()
    {
        Collection = "purchase-bills", Label = "Bill", Table = "purchase_bills", LinesTable = "purchase_bill_lines",
        LineDocument = "bill_id", NumberField = "bill_number", PartyField = "supplier_id", DateField = "bill_date",
        HasDueDate = true, PriceField = "unit_cost", AccountField = "expense_account_id", ProductAccount = "inventory_account_id",
        PartyTable = "suppliers", ControlColumn = "ap_account_id", Sales = false, IsCreditNote = false, DetailOnCredit = false,
        AppliedSql = "SELECT 1 FROM bill_applications WHERE bill_id = {0} UNION ALL SELECT 1 FROM purchase_credit_applications WHERE bill_id = {0}",
    };

    public static readonly DocKind SalesCreditNote = new()
    {
        Collection = "sales-credit-notes", Label = "Credit Note", Table = "sales_credit_notes", LinesTable = "sales_credit_note_lines",
        LineDocument = "credit_note_id", NumberField = "credit_note_number", PartyField = "customer_id", DateField = "credit_note_date",
        HasDueDate = false, PriceField = "unit_price", AccountField = "revenue_account_id", ProductAccount = "revenue_account_id",
        PartyTable = "customers", ControlColumn = "ar_account_id", Sales = true, IsCreditNote = true, DetailOnCredit = false,
        AppliedSql = "SELECT 1 FROM sales_credit_applications WHERE credit_note_id = {0}",
    };

    public static readonly DocKind PurchaseCreditNote = new()
    {
        Collection = "purchase-credit-notes", Label = "Credit Note", Table = "purchase_credit_notes", LinesTable = "purchase_credit_note_lines",
        LineDocument = "credit_note_id", NumberField = "credit_note_number", PartyField = "supplier_id", DateField = "credit_note_date",
        HasDueDate = false, PriceField = "unit_cost", AccountField = "expense_account_id", ProductAccount = "inventory_account_id",
        PartyTable = "suppliers", ControlColumn = "ap_account_id", Sales = false, IsCreditNote = true, DetailOnCredit = true,
        AppliedSql = "SELECT 1 FROM purchase_credit_applications WHERE credit_note_id = {0}",
    };
}

/// <summary>
/// Invoices, bills, and credit notes: drafts that are created, edited as a
/// whole (header and every line), deleted, posted to the ledger, and
/// unposted by an administrator (spec/api.md §5.9, spec/domain.md §4).
/// The database computes line amounts and keeps draft totals current.
/// </summary>
public sealed class Documents<TDoc, TLine, TBalance>(TadmorDb db, Journal journal, DocKind kind) : IDocuments
    where TDoc : LineDocument
    where TLine : DocumentLine, new()
    where TBalance : DocumentBalance
{
    public DocKind Kind => kind;

    /// <summary>A document's read shape, in its kind's vocabulary.</summary>
    public sealed record Row(TDoc Doc, TBalance? Balance);

    private IQueryable<DocAndBalance> Rows =>
        from d in db.Set<TDoc>().AsNoTracking()
        join b in db.Set<TBalance>() on d.Id equals b.DocumentId into bs
        from b in bs.DefaultIfEmpty()
        select new DocAndBalance { Doc = d, Balance = b };

    private sealed class DocAndBalance
    {
        public required TDoc Doc { get; init; }
        public TBalance? Balance { get; init; }
    }

    public async Task<List<Dictionary<string, object?>>> ListAsync()
    {
        var rows = await Rows.OrderByDescending(r => r.Doc.Date).ThenByDescending(r => r.Doc.Id).ToListAsync();
        return rows.Select(r => Shape(new Row(r.Doc, r.Balance))).ToList();
    }

    public async Task<Dictionary<string, object?>> GetAsync(int id) => Shape(await RowAsync(id));

    public async Task<Row> RowAsync(int id)
    {
        var r = await Rows.Where(r => r.Doc.Id == id).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();
        return new Row(r.Doc, r.Balance);
    }

    public Dictionary<string, object?> Shape(Row r)
    {
        var d = r.Doc;
        var shape = new Dictionary<string, object?>
        {
            ["id"] = d.Id,
            [kind.NumberField] = d.Number,
            [kind.PartyField] = d.PartyId,
            [kind.DateField] = d.Date,
        };
        if (kind.HasDueDate)
        {
            shape["due_date"] = d.DueDate;
        }
        shape[kind.SettlementField] = r.Balance?.SettlementStatus ?? (kind.IsCreditNote ? "open" : "unpaid");
        shape["currency_code"] = d.CurrencyCode;
        shape["status"] = d.Status;
        shape["subtotal"] = d.Subtotal;
        shape["tax_total"] = d.TaxTotal;
        shape["total"] = d.Total;
        shape["amount_applied"] = r.Balance?.AmountApplied ?? 0m;
        shape["balance"] = r.Balance?.Balance ?? d.Total;
        shape["journal_entry_id"] = d.JournalEntryId;
        shape["reference"] = d.Reference;
        shape["memo"] = d.Memo;
        return shape;
    }

    public async Task<List<Dictionary<string, object?>>> LinesAsync(int id)
    {
        if (!await db.Set<TDoc>().AnyAsync(d => d.Id == id))
        {
            throw ServiceException.NotFound();
        }
        var lines = await db.Set<TLine>().Where(l => l.DocumentId == id).OrderBy(l => l.LineNo).ToListAsync();
        return lines.Select(ShapeLine).ToList();
    }

    public Dictionary<string, object?> ShapeLine(DocumentLine l) => new()
    {
        ["id"] = l.Id,
        ["line_no"] = l.LineNo,
        ["product_id"] = l.ProductId,
        ["description"] = l.Description,
        ["quantity"] = l.Quantity,
        [kind.PriceField] = l.Price,
        ["tax_code"] = l.TaxCode,
        ["tax_rate"] = l.TaxRate,
        ["line_subtotal"] = l.LineSubtotal,
        ["tax_amount"] = l.TaxAmount,
        ["line_total"] = l.LineTotal,
        [kind.AccountField] = l.AccountId,
        ["order_line_id"] = l.OrderLineId,
    };

    /// <summary>The validated header and lines of a create or update body.</summary>
    private sealed record Body(string Number, int PartyId, DateOnly Date, DateOnly? DueDate, string Currency,
        string? Reference, string? Memo, List<TLine> Lines);

    private Body Parse(Input input)
    {
        var number = input.ReqStr(kind.NumberField);
        var party = input.ReqId(kind.PartyField);
        var date = input.ReqDate(kind.DateField);
        var due = kind.HasDueDate ? input.Date("due_date") : null;
        var currency = input.ReqStr("currency_code");
        var lines = input.List("lines").Select((l, i) => ParseLine(l, i + 1)).ToList();
        return new Body(number, party, date, due, currency, input.Str("reference"), input.Str("memo"), lines);
    }

    private TLine ParseLine(Input l, int lineNo)
    {
        var line = new TLine
        {
            LineNo = lineNo,
            Description = l.ReqStr("description"),
            ProductId = l.Int("product_id"),
            Quantity = l.Dec("quantity", Scale.Money) ?? 1,
            Price = l.Dec(kind.PriceField, Scale.Money) ?? 0,
            AccountId = l.Int(kind.AccountField),
            TaxCode = l.Str("tax_code"),
            TaxRate = l.Dec("tax_rate", Scale.Rate) ?? 0,
        };
        if (line.Quantity == 0)
        {
            throw ServiceException.Unprocessable($"line {lineNo}: quantity must not be zero");
        }
        return line;
    }

    /// <summary>Creates a draft and returns its id.</summary>
    public async Task<int> CreateAsync(Input input)
    {
        var body = Parse(input);
        var doc = (TDoc)Activator.CreateInstance(typeof(TDoc))!;
        Fill(doc, body);
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Set<TDoc>().Add(doc);
        await db.SaveChangesAsync();
        await InsertLinesAsync(doc.Id, body.Lines);
        await tx.CommitAsync();
        return doc.Id;
    }

    /// <summary>
    /// Replaces the header and the whole line set of a draft. A document
    /// produced from an order cannot be edited (spec/domain.md §6.4).
    /// </summary>
    public async Task UpdateAsync(int id, Input input)
    {
        var body = Parse(input);
        await using var tx = await db.Database.BeginTransactionAsync();
        var doc = await LockAsync(id);
        RequireDraft(doc);
        if (await db.Set<TLine>().AnyAsync(l => l.DocumentId == id && l.OrderLineId != null))
        {
            throw ServiceException.Conflict($"{kind.Label} {doc.Number} was produced from an order and cannot be edited");
        }
        Fill(doc, body);
        await db.SaveChangesAsync();
        await db.Set<TLine>().Where(l => l.DocumentId == id).ExecuteDeleteAsync();
        await InsertLinesAsync(id, body.Lines);
        await tx.CommitAsync();
    }

    /// <summary>Deletes a draft, order-produced ones included, which returns their quantities to the order.</summary>
    public async Task DeleteAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var doc = await LockAsync(id);
        RequireDraft(doc);
        db.Set<TDoc>().Remove(doc);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static void Fill(TDoc doc, Body body)
    {
        doc.Number = body.Number;
        doc.PartyId = body.PartyId;
        doc.Date = body.Date;
        doc.DueDate = body.DueDate;
        doc.CurrencyCode = body.Currency;
        doc.Reference = body.Reference;
        doc.Memo = body.Memo;
    }

    /// <summary>Adds lines to a document; the database fills in their amounts and the draft's totals.</summary>
    public async Task InsertLinesAsync(int documentId, IEnumerable<TLine> lines)
    {
        foreach (var line in lines)
        {
            line.DocumentId = documentId;
            db.Set<TLine>().Add(line);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>The document, locked for the rest of the transaction; 404 if there is none.</summary>
    private async Task<TDoc> LockAsync(int id)
    {
        if (await db.ScalarAsync<int?>($"SELECT id FROM {kind.Table} WHERE id = {{0}} FOR UPDATE", id) is null)
        {
            throw ServiceException.NotFound();
        }
        var doc = await db.Set<TDoc>().SingleAsync(d => d.Id == id);
        await db.Entry(doc).ReloadAsync();
        return doc;
    }

    private void RequireDraft(TDoc doc)
    {
        if (doc.Status != "draft")
        {
            throw ServiceException.Conflict($"{kind.Label} {doc.Number} is {doc.Status}, not a draft");
        }
    }

    /// <summary>
    /// Posts the document and returns its entry's id, after the checks of
    /// spec/domain.md §4.2 in order: it exists (404), is a draft (409), has a
    /// positive total, a control account, an account for every line with a
    /// subtotal and a tax account for every line with tax, an open period,
    /// and an exchange rate (all 422).
    ///
    /// Lines are summed per account, and tax per tax account; an account
    /// netting to zero gets no line, and one netting negative posts on the
    /// opposite side. Each detail line's base amount is round(amount × rate,
    /// 4); the control line carries the document total, and its base amount
    /// is the net of the detail lines' base amounts (spec/domain.md §7.2).
    /// </summary>
    public async Task<int> PostAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var doc = await LockAsync(id);
        RequireDraft(doc);
        if (doc.Total <= 0)
        {
            throw ServiceException.Unprocessable($"{kind.Label} {doc.Number} has nothing to post: its total is not positive");
        }
        var control = await db.ScalarAsync<int?>($"SELECT {kind.ControlColumn} FROM {kind.PartyTable} WHERE id = {{0}}", doc.PartyId)
            ?? throw ServiceException.Unprocessable(kind.Sales ? "the customer has no A/R account" : "the supplier has no A/P account");
        if (await db.ScalarAsync<bool>($$"""
            SELECT EXISTS (SELECT 1 FROM {{kind.LinesTable}} l LEFT JOIN products p ON p.id = l.product_id
                WHERE l.{{kind.LineDocument}} = {0} AND l.line_subtotal <> 0
                  AND COALESCE(l.{{kind.AccountField}}, p.{{kind.ProductAccount}}) IS NULL)
            """, id))
        {
            throw ServiceException.Unprocessable(
                $"a line has no {kind.AccountField.Replace("_id", "").Replace('_', ' ')}, and its product supplies none");
        }
        if (await db.ScalarAsync<bool>($$"""
            SELECT EXISTS (SELECT 1 FROM {{kind.LinesTable}} l LEFT JOIN tax_codes tc ON tc.code = l.tax_code
                WHERE l.{{kind.LineDocument}} = {0} AND l.tax_amount <> 0 AND tc.tax_account_id IS NULL)
            """, id))
        {
            throw ServiceException.Unprocessable("a taxed line has no tax code with a tax account");
        }

        var period = await journal.PeriodForAsync(doc.Date);
        var entry = await journal.CreateEntryAsync(doc.Date, period, doc.CurrencyCode, $"{kind.Label} {doc.Number}", doc.Number);

        // Detail lines; sign is +1 where they post on the credit side.
        var sign = kind.DetailOnCredit ? 1 : -1;
        await db.ExecAsync($$"""
            WITH detail AS (
                SELECT 0 AS ord, COALESCE(l.{{kind.AccountField}}, p.{{kind.ProductAccount}}) AS account_id,
                    sum(l.line_subtotal) AS amount, 'Detail' AS memo
                FROM {{kind.LinesTable}} l LEFT JOIN products p ON p.id = l.product_id
                WHERE l.{{kind.LineDocument}} = {0} GROUP BY 2 HAVING sum(l.line_subtotal) <> 0
                UNION ALL
                SELECT 1, tc.tax_account_id, sum(l.tax_amount), 'Tax'
                FROM {{kind.LinesTable}} l JOIN tax_codes tc ON tc.code = l.tax_code
                WHERE l.{{kind.LineDocument}} = {0} AND l.tax_amount <> 0 GROUP BY 2 HAVING sum(l.tax_amount) <> 0
            ), signed AS (
                SELECT ord, account_id, memo, {1} * amount AS credit_amount,
                    {1} * round(amount * (SELECT exchange_rate FROM journal_entries WHERE id = {2}), 4) AS credit_base
                FROM detail
            )
            INSERT INTO journal_lines (journal_entry_id, line_no, account_id, debit, credit, memo, base_debit, base_credit)
            SELECT {2}, row_number() OVER (ORDER BY ord, account_id), account_id,
                greatest(-credit_amount, 0), greatest(credit_amount, 0), memo,
                greatest(-credit_base, 0), greatest(credit_base, 0)
            FROM signed
            """, id, sign, entry);

        // The control line: the document total, on the other side, at the net of the detail base amounts.
        await db.ExecAsync("""
            INSERT INTO journal_lines (journal_entry_id, line_no, account_id, debit, credit, memo, base_debit, base_credit)
            SELECT {0}, count(*) + 1, {1},
                CASE WHEN {2} = 1 THEN {3}::numeric ELSE 0 END, CASE WHEN {2} = 1 THEN 0 ELSE {3}::numeric END, 'Control',
                greatest(COALESCE(sum(base_credit - base_debit), 0), 0), greatest(-COALESCE(sum(base_credit - base_debit), 0), 0)
            FROM journal_lines WHERE journal_entry_id = {0}
            """, entry, control, sign, doc.Total);

        doc.Status = "posted";
        doc.JournalEntryId = entry;
        doc.PeriodId = period;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return entry;
    }

    /// <summary>
    /// Unposts the document (administrators only) and returns the reversal's
    /// id: a mirror entry, the original left posted, and the document back in
    /// draft with no entry (spec/domain.md §4.4). Refused (409) if it is not
    /// posted, anything is applied to or from it, or its entry was already
    /// reversed or is matched on a bank statement, and (422) if no open
    /// period covers its date.
    /// </summary>
    public async Task<int> UnpostAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var doc = await LockAsync(id);
        if (doc.Status != "posted" || doc.JournalEntryId is not { } entry)
        {
            throw ServiceException.Conflict($"{kind.Label} {doc.Number} is {doc.Status}, not posted");
        }
        if (await db.ScalarAsync<bool>($"SELECT EXISTS ({kind.AppliedSql})", id))
        {
            throw ServiceException.Conflict($"{kind.Label} {doc.Number} has applications and cannot be unposted");
        }
        var reversal = await journal.ReverseAsync(entry);
        doc.Status = "draft";
        doc.JournalEntryId = null;
        doc.PeriodId = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return reversal;
    }
}
