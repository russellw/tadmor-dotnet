using Microsoft.EntityFrameworkCore;
using Tadmor.Db;
using Tadmor.Services;

namespace Tadmor.Ui;

/// <summary>A choice in a picker.</summary>
public sealed record Option(string Value, string Label);

/// <summary>
/// Names and pickers for the UI: the records a form may reference, and the
/// names lists show in place of ids. Pickers offer active records only
/// (spec/domain.md §13), plus whatever the record being edited already
/// references.
/// </summary>
public sealed class Lookups(TadmorDb db)
{
    private Dictionary<int, string>? organizations;
    private Dictionary<int, string>? customers;
    private Dictionary<int, string>? suppliers;
    private List<Account>? accounts;

    public async Task<Dictionary<int, string>> OrganizationNamesAsync() =>
        organizations ??= await db.Organizations.ToDictionaryAsync(o => o.Id, o => o.Name);

    /// <summary>Customer or supplier names: the organization's.</summary>
    public async Task<Dictionary<int, string>> PartyNamesAsync(bool sales)
    {
        if (sales)
        {
            return customers ??= await (from c in db.Customers join o in db.Organizations on c.OrganizationId equals o.Id
                                        select new { c.Id, o.Name }).ToDictionaryAsync(x => x.Id, x => x.Name);
        }
        return suppliers ??= await (from s in db.Suppliers join o in db.Organizations on s.OrganizationId equals o.Id
                                    select new { s.Id, o.Name }).ToDictionaryAsync(x => x.Id, x => x.Name);
    }

    public sealed record PartyOption(int Id, string Name, string? Currency, bool IsActive);

    public async Task<List<PartyOption>> PartiesAsync(bool sales)
    {
        var q = sales
            ? from c in db.Customers join o in db.Organizations on c.OrganizationId equals o.Id
              select new PartyOption(c.Id, o.Name, c.CurrencyCode, c.IsActive)
            : from s in db.Suppliers join o in db.Organizations on s.OrganizationId equals o.Id
              select new PartyOption(s.Id, o.Name, s.CurrencyCode, s.IsActive);
        return (await q.ToListAsync()).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<Account>> AccountsAsync() =>
        accounts ??= await db.Accounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync();

    public async Task<Dictionary<int, Account>> AccountMapAsync() => (await AccountsAsync()).ToDictionary(a => a.Id);

    public async Task<string> AccountLabelAsync(int? id) =>
        id is { } i && (await AccountMapAsync()).TryGetValue(i, out var a) ? $"{a.Code} {a.Name}" : "";

    /// <summary>Accounts a line or entry can post to: postable and active.</summary>
    public async Task<List<Option>> PostableAccountsAsync(Func<Account, bool>? filter = null) =>
        (await AccountsAsync()).Where(a => a.IsPostable && a.IsActive && (filter?.Invoke(a) ?? true))
        .Select(a => new Option(a.Id.ToString(), $"{a.Code} {a.Name}")).ToList();

    public async Task<List<Option>> AllActiveAccountsAsync(int? except = null) =>
        (await AccountsAsync()).Where(a => a.IsActive && a.Id != except)
        .Select(a => new Option(a.Id.ToString(), $"{a.Code} {a.Name}")).ToList();

    public Task<List<Product>> ProductsAsync() => db.Products.AsNoTracking().OrderBy(p => p.Sku).ToListAsync();

    public Task<List<TaxCode>> TaxCodesAsync() => db.TaxCodes.AsNoTracking().OrderBy(t => t.Code).ToListAsync();

    public async Task<List<Option>> TaxCodeOptionsAsync() =>
        (await TaxCodesAsync()).Where(t => t.IsActive).Select(t => new Option(t.Code, $"{t.Code} {t.Name}")).ToList();

    public async Task<List<Option>> PaymentTermOptionsAsync() =>
        await db.PaymentTerms.OrderBy(p => p.DueDays).ThenBy(p => p.Code).Select(p => new Option(p.Code, p.Code + " " + p.Name)).ToListAsync();

    public async Task<List<Option>> WarehouseOptionsAsync() =>
        await db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.Code).Select(w => new Option(w.Id.ToString(), w.Code + " " + w.Name)).ToListAsync();

    public Task<Dictionary<int, string>> WarehouseNamesAsync() => db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Code);

    public async Task<Dictionary<int, string>> ProductNamesAsync() =>
        await db.Products.ToDictionaryAsync(p => p.Id, p => p.Sku + " " + p.Name);

    public async Task<List<Option>> OrganizationOptionsAsync() =>
        (await db.Organizations.ToListAsync()).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
        .Select(o => new Option(o.Id.ToString(), o.Name)).ToList();

    public async Task<List<Option>> CurrencyOptionsAsync() =>
        await db.Currencies.OrderBy(c => c.Code).Select(c => new Option(c.Code, c.Code)).ToListAsync();

    public async Task<List<Option>> CountryOptionsAsync() =>
        await db.Countries.OrderBy(c => c.Code).Select(c => new Option(c.Code, c.Code + " " + c.Name)).ToListAsync();

    public Task<string> BaseCurrencyAsync() => db.GlSettings.Select(s => s.BaseCurrency).SingleAsync();
}

/// <summary>The home screen's figures (spec/domain.md §13 H1 to H4).</summary>
public sealed class Dashboard(TadmorDb db)
{
    public sealed class Outstanding
    {
        public string CurrencyCode { get; set; } = "";
        public decimal Total { get; set; }
        public decimal Overdue { get; set; }
    }

    public sealed class Due
    {
        public int Id { get; set; }
        public string Number { get; set; } = "";
        public string PartyName { get; set; } = "";
        public DateOnly? DueDate { get; set; }
        public string CurrencyCode { get; set; } = "";
        public decimal Balance { get; set; }
    }

    public sealed class Counts
    {
        public int OpenSalesOrders { get; set; }
        public int OpenPurchaseOrders { get; set; }
        public int DraftInvoices { get; set; }
        public int DraftBills { get; set; }
    }

    /// <summary>Posted documents with a positive balance, per currency, with the overdue part.</summary>
    public Task<List<Outstanding>> OutstandingAsync(bool receivables)
    {
        var view = receivables ? "sales_invoice_balances" : "purchase_bill_balances";
        return db.ListAsync<Outstanding>($$"""
            SELECT currency_code AS "CurrencyCode", sum(balance) AS "Total",
                COALESCE(sum(balance) FILTER (WHERE due_date < current_date), 0) AS "Overdue"
            FROM {{view}} WHERE status = 'posted' AND balance > 0
            GROUP BY currency_code ORDER BY currency_code
            """);
    }

    public async Task<Counts> CountsAsync() => (await db.ListAsync<Counts>("""
        SELECT (SELECT count(*)::int FROM sales_orders WHERE status = 'open') AS "OpenSalesOrders",
            (SELECT count(*)::int FROM purchase_orders WHERE status = 'open') AS "OpenPurchaseOrders",
            (SELECT count(*)::int FROM sales_invoices WHERE status = 'draft') AS "DraftInvoices",
            (SELECT count(*)::int FROM purchase_bills WHERE status = 'draft') AS "DraftBills"
        """)).Single();

    /// <summary>The most overdue posted invoices, oldest due date first.</summary>
    public Task<List<Due>> OverdueInvoicesAsync() => db.ListAsync<Due>("""
        SELECT b.invoice_id AS "Id", b.invoice_number AS "Number", o.name AS "PartyName", b.due_date AS "DueDate",
            b.currency_code AS "CurrencyCode", b.balance AS "Balance"
        FROM sales_invoice_balances b JOIN customers c ON c.id = b.customer_id JOIN organizations o ON o.id = c.organization_id
        WHERE b.status = 'posted' AND b.balance > 0 AND b.due_date < current_date
        ORDER BY b.due_date, b.invoice_id LIMIT 10
        """);

    /// <summary>Posted bills with a balance falling due within the next 14 days.</summary>
    public Task<List<Due>> BillsDueSoonAsync() => db.ListAsync<Due>("""
        SELECT b.bill_id AS "Id", b.bill_number AS "Number", o.name AS "PartyName", b.due_date AS "DueDate",
            b.currency_code AS "CurrencyCode", b.balance AS "Balance"
        FROM purchase_bill_balances b JOIN suppliers s ON s.id = b.supplier_id JOIN organizations o ON o.id = s.organization_id
        WHERE b.status = 'posted' AND b.balance > 0 AND b.due_date BETWEEN current_date AND current_date + 14
        ORDER BY b.due_date, b.bill_id
        """);
}
