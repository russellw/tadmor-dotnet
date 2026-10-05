using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Tadmor.Db;

namespace Tadmor.Ui;

/// <summary>How the UI names each collection.</summary>
public static class Names
{
    private static readonly Dictionary<string, (string Plural, string Singular)> Map = new()
    {
        ["sales-invoices"] = ("Invoices", "invoice"),
        ["purchase-bills"] = ("Bills", "bill"),
        ["sales-credit-notes"] = ("Credit notes", "credit note"),
        ["purchase-credit-notes"] = ("Supplier credits", "supplier credit"),
        ["customer-payments"] = ("Customer payments", "customer payment"),
        ["supplier-payments"] = ("Supplier payments", "supplier payment"),
        ["sales-orders"] = ("Sales orders", "sales order"),
        ["purchase-orders"] = ("Purchase orders", "purchase order"),
    };

    public static string Plural(string collection) => Map[collection].Plural;

    public static string Singular(string collection) => Map[collection].Singular;

    /// <summary>The singular, capitalized, for a title.</summary>
    public static string Title(string collection) => char.ToUpperInvariant(Singular(collection)[0]) + Singular(collection)[1..];

    public static string Party(bool sales) => sales ? "Customer" : "Supplier";
}

/// <summary>
/// A line-item form's content (spec/domain.md §13 D2 and O2): the header
/// fields and the lines as text, either as stored or as posted (when a
/// refused save is shown again), and the choices the line pickers offer.
/// </summary>
public sealed class LineForm
{
    public required bool Sales { get; init; }
    public required string PriceField { get; init; }
    public required string AccountField { get; init; }
    public Dictionary<string, string> Header { get; } = [];
    public List<Dictionary<string, string>> Lines { get; } = [];
    public List<Lookups.PartyOption> Parties { get; set; } = [];
    public List<Product> Products { get; set; } = [];
    public List<TaxCode> TaxCodes { get; set; } = [];
    public List<Option> Accounts { get; set; } = [];
    public List<Option> Currencies { get; set; } = [];

    public static readonly string[] LineFields = ["product_id", "description", "quantity", "price", "account", "tax_code", "tax_rate"];

    public string H(string name) => Header.GetValueOrDefault(name, "");

    /// <summary>The field's request name: price and account vary by side.</summary>
    public string Name(string field) => field switch
    {
        "price" => PriceField,
        "account" => AccountField,
        _ => field,
    };

    public void FromRecord(Dictionary<string, object?> header, IEnumerable<Dictionary<string, object?>> lines)
    {
        foreach (var (k, v) in header)
        {
            Header[k] = Format.Raw(v);
        }
        foreach (var l in lines)
        {
            Lines.Add(LineFields.ToDictionary(f => f, f => f == "quantity" || f == "price" || f == "tax_rate"
                ? Format.Qty(Format.Dec(l.GetValueOrDefault(Name(f)))) : Format.Raw(l.GetValueOrDefault(Name(f)))));
        }
    }

    public void FromPost(IFormCollection form)
    {
        foreach (var (k, v) in form.Where(kv => !kv.Key.StartsWith("line_", StringComparison.Ordinal)))
        {
            Header[k] = v.ToString();
        }
        var rows = LineFields.Max(f => form["line_" + Name(f)].Count);
        for (var i = 0; i < rows; i++)
        {
            Lines.Add(LineFields.ToDictionary(f => f, f => form["line_" + Name(f)] is var vs && i < vs.Count ? vs[i] ?? "" : ""));
        }
    }

    /// <summary>What the line editor's script needs: product defaults, tax rates, and party currencies.</summary>
    public string ClientData() => JsonSerializer.Serialize(new
    {
        products = Products.ToDictionary(p => p.Id.ToString(), p => new
        {
            description = p.Name,
            tax_code = p.TaxCode,
            // Sales lines take the product's price and revenue account too.
            price = Sales ? Format.Qty(p.UnitPrice) : null,
            account = Sales ? p.RevenueAccountId?.ToString() : null,
        }),
        taxes = TaxCodes.ToDictionary(t => t.Code, t => Format.Qty(t.Rate)),
        partyCurrency = Parties.Where(p => p.Currency is not null).ToDictionary(p => p.Id.ToString(), p => p.Currency),
    });

    public async Task LoadChoicesAsync(Lookups lookups)
    {
        Parties = (await lookups.PartiesAsync(Sales)).Where(p => p.IsActive || p.Id.ToString() == H(Sales ? "customer_id" : "supplier_id")).ToList();
        Products = (await lookups.ProductsAsync()).Where(p => p.IsActive || Lines.Any(l => l["product_id"] == p.Id.ToString())).ToList();
        TaxCodes = (await lookups.TaxCodesAsync()).Where(t => t.IsActive || Lines.Any(l => l["tax_code"] == t.Code)).ToList();
        Accounts = await lookups.PostableAccountsAsync();
        Currencies = await lookups.CurrencyOptionsAsync();
    }
}
