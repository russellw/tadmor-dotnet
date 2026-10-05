using System.Globalization;
using System.Text.Json;
using Tadmor.Printing;

namespace Tadmor.Ui;

/// <summary>
/// Display formatting (spec/domain.md §13 G7): amounts exact and grouped,
/// never rounded, with at least two places; quantities and rates without
/// insignificant zeros.
/// </summary>
public static class Format
{
    /// <summary>Today, the UTC date (spec/api.md §1.2).</summary>
    public static DateOnly Today => Services.Stock.Today;

    public static string Amount(decimal? d) => d is { } v ? Layout.Amount(v) : "";

    public static string Qty(decimal? d) => d is { } v ? Layout.Qty(v) : "";

    public static string Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>An amount with its currency, as foreign documents show them.</summary>
    public static string Money(decimal? d, string? currency) => d is null ? "" : $"{currency} {Amount(d)}".Trim();

    /// <summary>A value from a service's read shape, as text for a list cell or a form field.</summary>
    public static string Raw(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        DateOnly d => Date(d),
        bool b => b ? "true" : "false",
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            JsonValueKind.String => e.GetString()!,
            _ => e.GetRawText(),
        },
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    public static decimal? Dec(object? v) =>
        decimal.TryParse(Raw(v), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static int? Int(object? v) => int.TryParse(Raw(v), out var i) ? i : null;
}
