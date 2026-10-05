using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

// Master data (spec/api.md §5.2 to §5.6). Records are never deleted, only
// deactivated through update, which is a full replacement: an omitted
// boolean is false and an omitted optional field null. On create, is_active
// is ignored and records start active. The schema refuses the rest: a
// duplicate key is a 409, and an unknown reference, a value its checks
// reject, or a self-parented account is a 422 (DatabaseErrors).

public sealed class Organizations(TadmorDb db)
{
    public sealed record Dto(int Id, string Name, string? LegalName, string? TaxId, string? CountryCode,
        string? DefaultCurrency, string? Email, bool IsSelf);

    private static readonly Expression<Func<Organization, Dto>> Rows =
        o => new Dto(o.Id, o.Name, o.LegalName, o.TaxId, o.CountryCode, o.DefaultCurrency, o.Email, o.IsSelf);

    public Task<List<Dto>> ListAsync() => db.Organizations.OrderBy(o => o.Name).ThenBy(o => o.Id).Select(Rows).ToListAsync();

    public async Task<Dto> GetAsync(int id) => await db.Organizations.Where(o => o.Id == id).Select(Rows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    public async Task<int> CreateAsync(Input input)
    {
        var o = new Organization { Name = "" };
        Fill(o, input);
        db.Organizations.Add(o);
        await db.SaveChangesAsync();
        return o.Id;
    }

    public async Task UpdateAsync(int id, Input input)
    {
        input.ReqStr("name");
        var o = await db.Organizations.FindAsync(id) ?? throw ServiceException.NotFound();
        Fill(o, input);
        await db.SaveChangesAsync();
    }

    private static void Fill(Organization o, Input input)
    {
        o.Name = input.ReqStr("name");
        o.LegalName = input.Str("legal_name");
        o.TaxId = input.Str("tax_id");
        o.CountryCode = input.Str("country_code");
        o.DefaultCurrency = input.Str("default_currency");
        o.Email = input.Str("email");
        o.IsSelf = input.Bool("is_self");
    }
}

/// <summary>Customers and suppliers: roles on an organization, at most one of each per organization.</summary>
public sealed class Parties(TadmorDb db)
{
    public sealed record CustomerDto(int Id, int OrganizationId, string? CustomerNumber, int? ArAccountId,
        string? PaymentTermsCode, string? CurrencyCode, string? TaxCode, decimal? CreditLimit, bool IsActive);

    public sealed record SupplierDto(int Id, int OrganizationId, string? SupplierNumber, int? ApAccountId,
        string? PaymentTermsCode, string? CurrencyCode, string? TaxCode, bool IsActive);

    private static readonly Expression<Func<Customer, CustomerDto>> CustomerRows =
        c => new CustomerDto(c.Id, c.OrganizationId,
        c.Number, c.ControlAccountId, c.PaymentTermsCode, c.CurrencyCode, c.TaxCode, c.CreditLimit, c.IsActive);

    private static readonly Expression<Func<Supplier, SupplierDto>> SupplierRows =
        s => new SupplierDto(s.Id, s.OrganizationId,
        s.Number, s.ControlAccountId, s.PaymentTermsCode, s.CurrencyCode, s.TaxCode, s.IsActive);

    public Task<List<CustomerDto>> CustomersAsync() => db.Customers.OrderBy(c => c.Id).Select(CustomerRows).ToListAsync();

    public async Task<CustomerDto> CustomerAsync(int id) =>
        await db.Customers.Where(c => c.Id == id).Select(CustomerRows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    public Task<List<SupplierDto>> SuppliersAsync() => db.Suppliers.OrderBy(s => s.Id).Select(SupplierRows).ToListAsync();

    public async Task<SupplierDto> SupplierAsync(int id) =>
        await db.Suppliers.Where(s => s.Id == id).Select(SupplierRows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    public async Task<int> CreateCustomerAsync(Input input)
    {
        var c = new Customer();
        FillCustomer(c, input);
        c.IsActive = true;
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    public async Task UpdateCustomerAsync(int id, Input input)
    {
        input.ReqId("organization_id");
        var c = await db.Customers.FindAsync(id) ?? throw ServiceException.NotFound();
        FillCustomer(c, input);
        await db.SaveChangesAsync();
    }

    public async Task<int> CreateSupplierAsync(Input input)
    {
        var s = new Supplier();
        Fill(s, input, "supplier_number", "ap_account_id");
        s.IsActive = true;
        db.Suppliers.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    public async Task UpdateSupplierAsync(int id, Input input)
    {
        input.ReqId("organization_id");
        var s = await db.Suppliers.FindAsync(id) ?? throw ServiceException.NotFound();
        Fill(s, input, "supplier_number", "ap_account_id");
        await db.SaveChangesAsync();
    }

    private static void FillCustomer(Customer c, Input input)
    {
        Fill(c, input, "customer_number", "ar_account_id");
        c.CreditLimit = input.Dec("credit_limit", Scale.Money);
    }

    private static void Fill(Party p, Input input, string number, string control)
    {
        p.OrganizationId = input.ReqId("organization_id");
        p.Number = input.Str(number);
        p.ControlAccountId = input.Int(control);
        p.PaymentTermsCode = input.Str("payment_terms_code");
        p.CurrencyCode = input.Str("currency_code");
        p.TaxCode = input.Str("tax_code");
        p.IsActive = input.Bool("is_active");
    }
}

public sealed class Products(TadmorDb db)
{
    public sealed record Dto(int Id, string Sku, string Name, string? Description, decimal UnitPrice, string? CurrencyCode,
        int? RevenueAccountId, string? TaxCode, bool TrackInventory, int? InventoryAccountId, int? CogsAccountId, bool IsActive);

    private static readonly Expression<Func<Product, Dto>> Rows =
        p => new Dto(p.Id, p.Sku, p.Name, p.Description, p.UnitPrice,
        p.CurrencyCode, p.RevenueAccountId, p.TaxCode, p.TrackInventory, p.InventoryAccountId, p.CogsAccountId, p.IsActive);

    public Task<List<Dto>> ListAsync() => db.Products.OrderBy(p => p.Sku).ThenBy(p => p.Id).Select(Rows).ToListAsync();

    public async Task<Dto> GetAsync(int id) => await db.Products.Where(p => p.Id == id).Select(Rows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    public async Task<int> CreateAsync(Input input)
    {
        var p = new Product { Sku = "", Name = "" };
        Fill(p, input);
        p.IsActive = true;
        db.Products.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    public async Task UpdateAsync(int id, Input input)
    {
        input.ReqStr("sku");
        input.ReqStr("name");
        var p = await db.Products.FindAsync(id) ?? throw ServiceException.NotFound();
        Fill(p, input);
        await db.SaveChangesAsync();
    }

    private static void Fill(Product p, Input input)
    {
        p.Sku = input.ReqStr("sku");
        p.Name = input.ReqStr("name");
        p.Description = input.Str("description");
        p.UnitPrice = input.Dec("unit_price", Scale.Money) ?? 0;
        p.CurrencyCode = input.Str("currency_code");
        p.RevenueAccountId = input.Int("revenue_account_id");
        p.TaxCode = input.Str("tax_code");
        p.TrackInventory = input.Bool("track_inventory");
        p.InventoryAccountId = input.Int("inventory_account_id");
        p.CogsAccountId = input.Int("cogs_account_id");
        p.IsActive = input.Bool("is_active");
    }
}

/// <summary>The chart of accounts (spec/api.md §5.5).</summary>
public sealed class Accounts(TadmorDb db)
{
    public sealed record Dto(int Id, string Code, string Name, string AccountType, int? ParentId, string? CurrencyCode,
        bool IsPostable, bool IsActive, bool IsCash, string CashFlowActivity);

    private static readonly Expression<Func<Account, Dto>> Rows =
        a => new Dto(a.Id, a.Code, a.Name, a.AccountType, a.ParentId,
        a.CurrencyCode, a.IsPostable, a.IsActive, a.IsCash, a.CashFlowActivity);

    public Task<List<Dto>> ListAsync() => db.Accounts.OrderBy(a => a.Code).ThenBy(a => a.Id).Select(Rows).ToListAsync();

    public async Task<Dto> GetAsync(int id) => await db.Accounts.Where(a => a.Id == id).Select(Rows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    /// <summary>Created active; is_postable defaults to false, which makes a summary account.</summary>
    public async Task<int> CreateAsync(Input input)
    {
        var a = new Account { Code = "", Name = "", AccountType = "" };
        Fill(a, input);
        a.IsActive = true;
        db.Accounts.Add(a);
        await db.SaveChangesAsync();
        return a.Id;
    }

    public async Task UpdateAsync(int id, Input input)
    {
        Required(input);
        var a = await db.Accounts.FindAsync(id) ?? throw ServiceException.NotFound();
        Fill(a, input);
        await db.SaveChangesAsync();
    }

    private static void Required(Input input)
    {
        input.ReqStr("code");
        input.ReqStr("name");
        input.ReqStr("account_type");
    }

    private static void Fill(Account a, Input input)
    {
        Required(input);
        a.Code = input.ReqStr("code");
        a.Name = input.ReqStr("name");
        a.AccountType = input.ReqStr("account_type");
        a.ParentId = input.Int("parent_id");
        a.CurrencyCode = input.Str("currency_code");
        a.IsPostable = input.Bool("is_postable");
        a.IsActive = input.Bool("is_active");
        a.IsCash = input.Bool("is_cash");
        a.CashFlowActivity = input.Str("cash_flow_activity") ?? "operating";
    }
}

/// <summary>Tax codes, payment terms, and warehouses (spec/api.md §5.6).</summary>
public sealed class Catalog(TadmorDb db)
{
    public sealed record TaxCodeDto(string Code, string Name, decimal Rate, int? TaxAccountId, bool IsActive);

    public sealed record PaymentTermDto(string Code, string Name, int DueDays);

    public sealed record WarehouseDto(int Id, string Code, string Name, int? AddressId, bool IsActive);

    private static readonly Expression<Func<TaxCode, TaxCodeDto>> TaxCodeRows =
        t => new TaxCodeDto(t.Code, t.Name, t.Rate, t.TaxAccountId, t.IsActive);

    public Task<List<TaxCodeDto>> TaxCodesAsync() => db.TaxCodes.OrderBy(t => t.Code).Select(TaxCodeRows).ToListAsync();

    public async Task<TaxCodeDto> TaxCodeAsync(string code) =>
        await db.TaxCodes.Where(t => t.Code == code).Select(TaxCodeRows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    public async Task<string> CreateTaxCodeAsync(Input input)
    {
        var t = new TaxCode { Code = input.ReqStr("code"), Name = input.ReqStr("name") };
        FillTaxCode(t, input);
        t.IsActive = true;
        db.TaxCodes.Add(t);
        await db.SaveChangesAsync();
        return t.Code;
    }

    /// <summary>The path's code wins over the body's.</summary>
    public async Task UpdateTaxCodeAsync(string code, Input input)
    {
        var name = input.ReqStr("name");
        var t = await db.TaxCodes.FindAsync(code) ?? throw ServiceException.NotFound();
        t.Name = name;
        FillTaxCode(t, input);
        await db.SaveChangesAsync();
    }

    private static void FillTaxCode(TaxCode t, Input input)
    {
        t.Rate = input.Dec("rate", Scale.Rate) ?? 0;
        t.TaxAccountId = input.Int("tax_account_id");
        t.IsActive = input.Bool("is_active");
    }

    private static readonly Expression<Func<PaymentTerm, PaymentTermDto>> TermRows =
        p => new PaymentTermDto(p.Code, p.Name, p.DueDays);

    public Task<List<PaymentTermDto>> PaymentTermsAsync() => db.PaymentTerms.OrderBy(p => p.DueDays).ThenBy(p => p.Code).Select(TermRows).ToListAsync();

    public async Task<PaymentTermDto> PaymentTermAsync(string code) =>
        await db.PaymentTerms.Where(p => p.Code == code).Select(TermRows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    public async Task<string> CreatePaymentTermAsync(Input input)
    {
        var p = new PaymentTerm { Code = input.ReqStr("code"), Name = input.ReqStr("name"), DueDays = input.Int("due_days") ?? 0 };
        db.PaymentTerms.Add(p);
        await db.SaveChangesAsync();
        return p.Code;
    }

    /// <summary>The path's code wins.</summary>
    public async Task UpdatePaymentTermAsync(string code, Input input)
    {
        var name = input.ReqStr("name");
        var days = input.Int("due_days") ?? 0;
        var p = await db.PaymentTerms.FindAsync(code) ?? throw ServiceException.NotFound();
        p.Name = name;
        p.DueDays = days;
        await db.SaveChangesAsync();
    }

    private static readonly Expression<Func<Warehouse, WarehouseDto>> WarehouseRows =
        w => new WarehouseDto(w.Id, w.Code, w.Name, w.AddressId, w.IsActive);

    public Task<List<WarehouseDto>> WarehousesAsync() => db.Warehouses.OrderBy(w => w.Code).ThenBy(w => w.Id).Select(WarehouseRows).ToListAsync();

    public async Task<WarehouseDto> WarehouseAsync(int id) =>
        await db.Warehouses.Where(w => w.Id == id).Select(WarehouseRows).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    public async Task<int> CreateWarehouseAsync(Input input)
    {
        var w = new Warehouse { Code = input.ReqStr("code"), Name = input.ReqStr("name"), AddressId = input.Int("address_id") };
        db.Warehouses.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    public async Task UpdateWarehouseAsync(int id, Input input)
    {
        var code = input.ReqStr("code");
        var name = input.ReqStr("name");
        var w = await db.Warehouses.FindAsync(id) ?? throw ServiceException.NotFound();
        w.Code = code;
        w.Name = name;
        w.AddressId = input.Int("address_id");
        w.IsActive = input.Bool("is_active");
        await db.SaveChangesAsync();
    }
}
