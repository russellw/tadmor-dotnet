using System.Text.Json;
using Tadmor.Api;
using Tadmor.Services;

namespace Tadmor.Ui;

/// <summary>How a list cell renders.</summary>
public enum Cell { Text, Amount, Qty, Flag, Active, Organization, Role }

public sealed record Column(string Name, string Label, Cell Cell = Cell.Text);

public enum FieldType { Text, Email, Password, Int, Decimal, Flag, Select }

/// <summary>
/// A form field, named as in the API. Options supplies a picker's choices;
/// CreateOnly and EditOnly fields appear on one form only, and Locked
/// fields are shown but not editable once the record exists.
/// </summary>
public sealed record Field(string Name, string Label, FieldType Type = FieldType.Text)
{
    public bool Required { get; init; }
    public bool CreateOnly { get; init; }
    public bool EditOnly { get; init; }
    public bool Locked { get; init; }
    public Func<Lookups, string?, Task<List<Option>>>? Options { get; init; }
}

/// <summary>
/// One master-data resource: its list, its form, and the service calls
/// behind them (spec/domain.md §13 M1 to M7). Keys are ids, or codes for
/// tax codes and payment terms.
/// </summary>
public sealed record Resource
{
    public required string Path { get; init; }
    public required string Title { get; init; }
    public required string Singular { get; init; }
    public string KeyField { get; init; } = "id";
    public bool AdminOnly { get; init; }
    public required IReadOnlyList<Column> Columns { get; init; }
    public required IReadOnlyList<Field> Fields { get; init; }
    public required Func<IServiceProvider, Task<object>> List { get; init; }
    public required Func<IServiceProvider, string, Task<object>> Get { get; init; }
    public required Func<IServiceProvider, Input, Task<string>> Create { get; init; }
    public required Func<IServiceProvider, string, Input, CurrentUser, Task> Update { get; init; }

    /// <summary>A service's read shape as name → value, in the API's vocabulary.</summary>
    public static Dictionary<string, object?> Row(object dto) =>
        JsonSerializer.SerializeToElement(dto, Json.Options).EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());

    public static IEnumerable<Dictionary<string, object?>> Rows(object list) =>
        JsonSerializer.SerializeToElement(list, Json.Options).EnumerateArray()
            .Select(e => e.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone()));

    private static int Id(string key) => int.TryParse(key, out var id) && id > 0 ? id : throw ServiceException.NotFound();

    private static T S<T>(IServiceProvider sp) where T : notnull => sp.GetRequiredService<T>();

    // ---- pickers ----

    private static Func<Lookups, string?, Task<List<Option>>> Pick(Func<Lookups, Task<List<Option>>> source) =>
        async (l, current) => Keep(await source(l), current);

    /// <summary>The choices, plus the current value if it is no longer offered (an inactive record).</summary>
    private static List<Option> Keep(List<Option> options, string? current)
    {
        if (!string.IsNullOrEmpty(current) && options.All(o => o.Value != current))
        {
            options.Insert(0, new Option(current, current + " (inactive)"));
        }
        return options;
    }

    private static List<Option> Fixed(params string[] values) => values.Select(v => new Option(v, v)).ToList();

    private static readonly Field Active = new("is_active", "Active", FieldType.Flag) { EditOnly = true };

    private static readonly Field Currency = new("currency_code", "Currency", FieldType.Select) { Options = Pick(l => l.CurrencyOptionsAsync()) };

    private static readonly Field TaxCodeField = new("tax_code", "Tax code", FieldType.Select) { Options = Pick(l => l.TaxCodeOptionsAsync()) };

    private static readonly Field Terms = new("payment_terms_code", "Payment terms", FieldType.Select)
    {
        Options = Pick(l => l.PaymentTermOptionsAsync()),
    };

    private static Field AccountField(string name, string label, Func<Db.Account, bool>? filter = null) =>
        new(name, label, FieldType.Select) { Options = Pick(l => l.PostableAccountsAsync(filter)) };

    public static readonly IReadOnlyList<Resource> All =
    [
        new()
        {
            Path = "organizations", Title = "Organizations", Singular = "organization",
            Columns = [new("name", "Name"), new("legal_name", "Legal name"), new("tax_id", "Tax id"), new("country_code", "Country"),
                new("default_currency", "Currency"), new("is_self", "Our company", Cell.Flag)],
            Fields = [new("name", "Name") { Required = true }, new("legal_name", "Legal name"), new("tax_id", "Tax id"),
                new("country_code", "Country", FieldType.Select) { Options = Pick(l => l.CountryOptionsAsync()) },
                new("default_currency", "Default currency", FieldType.Select) { Options = Pick(l => l.CurrencyOptionsAsync()) },
                new("email", "Email", FieldType.Email), new("is_self", "This is our own company", FieldType.Flag)],
            List = async sp => await S<Organizations>(sp).ListAsync(),
            Get = async (sp, k) => await S<Organizations>(sp).GetAsync(Id(k)),
            Create = async (sp, i) => (await S<Organizations>(sp).CreateAsync(i)).ToString(),
            Update = (sp, k, i, _) => S<Organizations>(sp).UpdateAsync(Id(k), i),
        },
        new()
        {
            Path = "customers", Title = "Customers", Singular = "customer",
            Columns = [new("organization_id", "Organization", Cell.Organization), new("customer_number", "Number"),
                new("currency_code", "Currency"), new("tax_code", "Tax code"), new("payment_terms_code", "Terms"),
                new("credit_limit", "Credit limit", Cell.Amount), new("is_active", "Status", Cell.Active)],
            Fields = [new("organization_id", "Organization", FieldType.Select) { Required = true, Locked = true, Options = Pick(l => l.OrganizationOptionsAsync()) },
                new("customer_number", "Customer number"), AccountField("ar_account_id", "A/R account", a => a.AccountType == "asset"),
                Terms, Currency, TaxCodeField, new("credit_limit", "Credit limit", FieldType.Decimal), Active],
            List = async sp => await S<Parties>(sp).CustomersAsync(),
            Get = async (sp, k) => await S<Parties>(sp).CustomerAsync(Id(k)),
            Create = async (sp, i) => (await S<Parties>(sp).CreateCustomerAsync(i)).ToString(),
            Update = (sp, k, i, _) => S<Parties>(sp).UpdateCustomerAsync(Id(k), i),
        },
        new()
        {
            Path = "suppliers", Title = "Suppliers", Singular = "supplier",
            Columns = [new("organization_id", "Organization", Cell.Organization), new("supplier_number", "Number"),
                new("currency_code", "Currency"), new("tax_code", "Tax code"), new("payment_terms_code", "Terms"),
                new("is_active", "Status", Cell.Active)],
            Fields = [new("organization_id", "Organization", FieldType.Select) { Required = true, Locked = true, Options = Pick(l => l.OrganizationOptionsAsync()) },
                new("supplier_number", "Supplier number"), AccountField("ap_account_id", "A/P account", a => a.AccountType == "liability"),
                Terms, Currency, TaxCodeField, Active],
            List = async sp => await S<Parties>(sp).SuppliersAsync(),
            Get = async (sp, k) => await S<Parties>(sp).SupplierAsync(Id(k)),
            Create = async (sp, i) => (await S<Parties>(sp).CreateSupplierAsync(i)).ToString(),
            Update = (sp, k, i, _) => S<Parties>(sp).UpdateSupplierAsync(Id(k), i),
        },
        new()
        {
            Path = "products", Title = "Products", Singular = "product",
            Columns = [new("sku", "SKU"), new("name", "Name"), new("unit_price", "Unit price", Cell.Amount), new("currency_code", "Currency"),
                new("tax_code", "Tax code"), new("track_inventory", "Stocked", Cell.Flag), new("is_active", "Status", Cell.Active)],
            Fields = [new("sku", "SKU") { Required = true }, new("name", "Name") { Required = true }, new("description", "Description"),
                new("unit_price", "Unit price", FieldType.Decimal), Currency, TaxCodeField,
                AccountField("revenue_account_id", "Revenue account", a => a.AccountType == "revenue"),
                new("track_inventory", "Track inventory", FieldType.Flag),
                AccountField("inventory_account_id", "Inventory account"), AccountField("cogs_account_id", "COGS account", a => a.AccountType == "expense"),
                Active],
            List = async sp => await S<Products>(sp).ListAsync(),
            Get = async (sp, k) => await S<Products>(sp).GetAsync(Id(k)),
            Create = async (sp, i) => (await S<Products>(sp).CreateAsync(i)).ToString(),
            Update = (sp, k, i, _) => S<Products>(sp).UpdateAsync(Id(k), i),
        },
        new()
        {
            Path = "accounts", Title = "Chart of accounts", Singular = "account",
            Columns = [new("code", "Code"), new("name", "Name"), new("account_type", "Type"), new("currency_code", "Currency"),
                new("is_postable", "Postable", Cell.Flag), new("is_active", "Status", Cell.Active)],
            Fields = [new("code", "Code") { Required = true }, new("name", "Name") { Required = true },
                new("account_type", "Type", FieldType.Select) { Required = true, Options = (_, _) => Task.FromResult(Fixed("asset", "liability", "equity", "revenue", "expense")) },
                // The parent picker never offers the account itself (M4); the form passes its id as current.
                new("parent_id", "Parent", FieldType.Select) { Options = async (l, self) => await l.AllActiveAccountsAsync(int.TryParse(self, out var s) ? s : null) },
                Currency, new("is_postable", "Postable (carries journal lines)", FieldType.Flag),
                new("is_cash", "Cash or bank account", FieldType.Flag),
                new("cash_flow_activity", "Cash-flow activity", FieldType.Select) { Options = (_, _) => Task.FromResult(Fixed("operating", "investing", "financing")) },
                Active],
            List = async sp => await S<Accounts>(sp).ListAsync(),
            Get = async (sp, k) => await S<Accounts>(sp).GetAsync(Id(k)),
            Create = async (sp, i) => (await S<Accounts>(sp).CreateAsync(i)).ToString(),
            Update = (sp, k, i, _) => S<Accounts>(sp).UpdateAsync(Id(k), i),
        },
        new()
        {
            Path = "tax-codes", Title = "Tax codes", Singular = "tax code", KeyField = "code",
            Columns = [new("code", "Code"), new("name", "Name"), new("rate", "Rate %", Cell.Qty), new("is_active", "Status", Cell.Active)],
            Fields = [new("code", "Code") { Required = true, Locked = true }, new("name", "Name") { Required = true },
                new("rate", "Rate (percent)", FieldType.Decimal), AccountField("tax_account_id", "Tax account"), Active],
            List = async sp => await S<Catalog>(sp).TaxCodesAsync(),
            Get = async (sp, k) => await S<Catalog>(sp).TaxCodeAsync(k),
            Create = (sp, i) => S<Catalog>(sp).CreateTaxCodeAsync(i),
            Update = (sp, k, i, _) => S<Catalog>(sp).UpdateTaxCodeAsync(k, i),
        },
        new()
        {
            Path = "payment-terms", Title = "Payment terms", Singular = "payment term", KeyField = "code",
            Columns = [new("code", "Code"), new("name", "Name"), new("due_days", "Due days")],
            Fields = [new("code", "Code") { Required = true, Locked = true }, new("name", "Name") { Required = true },
                new("due_days", "Due days", FieldType.Int)],
            List = async sp => await S<Catalog>(sp).PaymentTermsAsync(),
            Get = async (sp, k) => await S<Catalog>(sp).PaymentTermAsync(k),
            Create = (sp, i) => S<Catalog>(sp).CreatePaymentTermAsync(i),
            Update = (sp, k, i, _) => S<Catalog>(sp).UpdatePaymentTermAsync(k, i),
        },
        new()
        {
            Path = "warehouses", Title = "Warehouses", Singular = "warehouse",
            Columns = [new("code", "Code"), new("name", "Name"), new("is_active", "Status", Cell.Active)],
            Fields = [new("code", "Code") { Required = true }, new("name", "Name") { Required = true }, Active],
            List = async sp => await S<Catalog>(sp).WarehousesAsync(),
            Get = async (sp, k) => await S<Catalog>(sp).WarehouseAsync(Id(k)),
            Create = async (sp, i) => (await S<Catalog>(sp).CreateWarehouseAsync(i)).ToString(),
            Update = (sp, k, i, _) => S<Catalog>(sp).UpdateWarehouseAsync(Id(k), i),
        },
        new()
        {
            Path = "users", Title = "Users", Singular = "user", AdminOnly = true,
            Columns = [new("email", "Email"), new("full_name", "Name"), new("is_admin", "Role", Cell.Role), new("is_active", "Status", Cell.Active)],
            Fields = [new("email", "Email", FieldType.Email) { Required = true }, new("full_name", "Name") { Required = true },
                new("password", "Password (at least 8 characters)", FieldType.Password) { Required = true, CreateOnly = true },
                new("is_admin", "Administrator", FieldType.Flag), Active],
            List = async sp => await S<Users>(sp).ListAsync(),
            Get = async (sp, k) => await S<Users>(sp).GetAsync(Id(k)),
            Create = async (sp, i) => (await S<Users>(sp).CreateAsync(i)).ToString(),
            Update = (sp, k, i, me) => S<Users>(sp).UpdateAsync(Id(k), i, me),
        },
    ];

    public static Resource? Find(string path) => All.FirstOrDefault(r => r.Path == path);
}
