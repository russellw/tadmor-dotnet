using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tadmor.Services;

/// <summary>
/// One JSON request object, read field by field with the spec's rules
/// (spec/api.md §1.2 to §1.4): unknown fields are ignored; a field of the
/// wrong JSON type, or a missing required one, is a 400; a well-formed field
/// whose value is unacceptable (a malformed decimal or date, a value out of
/// range) is a 422. Decimals are rounded to their stored scale, half away
/// from zero, before anything checks or uses them. The UI builds the same
/// object from its forms, so both see one set of rules.
/// </summary>
public sealed partial class Input
{
    private readonly JsonElement root;

    private Input(JsonElement root) => this.root = root;

    public static Input Empty { get; } = new(JsonDocument.Parse("{}").RootElement);

    /// <summary>Parses a request body; an empty body is an empty object.</summary>
    public static Input Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw ServiceException.BadRequest("the request body is not valid JSON");
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw ServiceException.BadRequest("the request body must be a JSON object");
        }
        return new Input(root);
    }

    public static Input From(JsonElement element) => new(element);

    private JsonElement? Get(string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    public bool Has(string name) => Get(name) is not null;

    /// <summary>A string, trimmed; null when absent, null, or blank.</summary>
    public string? Str(string name)
    {
        if (Get(name) is not { } v)
        {
            return null;
        }
        if (v.ValueKind != JsonValueKind.String)
        {
            throw ServiceException.BadRequest($"{name} must be a string");
        }
        var s = v.GetString()!.Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>A required string: 400 when absent or blank.</summary>
    public string ReqStr(string name) => Str(name) ?? throw ServiceException.BadRequest($"{name} is required");

    /// <summary>An integer; null when absent or null.</summary>
    public int? Int(string name)
    {
        if (Get(name) is not { } v)
        {
            return null;
        }
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var i))
        {
            throw ServiceException.BadRequest($"{name} must be an integer");
        }
        return i;
    }

    /// <summary>A required reference: 400 unless a positive integer.</summary>
    public int ReqId(string name)
    {
        var i = Int(name);
        return i is > 0 ? i.Value : throw ServiceException.BadRequest($"{name} is required");
    }

    /// <summary>A boolean, false when absent (an update is a full replacement).</summary>
    public bool Bool(string name)
    {
        if (Get(name) is not { } v)
        {
            return false;
        }
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw ServiceException.BadRequest($"{name} must be a boolean"),
        };
    }

    /// <summary>
    /// A decimal at the given scale, below the given magnitude; null when
    /// absent. Strings are the spec's form; plain JSON numbers are accepted
    /// too, by their exact text.
    /// </summary>
    public decimal? Dec(string name, Scale scale)
    {
        if (Get(name) is not { } v)
        {
            return null;
        }
        string text = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()!.Trim(),
            JsonValueKind.Number => v.GetRawText(),
            _ => throw ServiceException.BadRequest($"{name} must be a decimal string"),
        };
        if (text.Length == 0)
        {
            return null;
        }
        return ParseDecimal(text, scale, name);
    }

    public decimal ReqDec(string name, Scale scale) =>
        Dec(name, scale) ?? throw ServiceException.BadRequest($"{name} is required");

    internal static decimal ParseDecimal(string text, Scale scale, string name)
    {
        if (!DecimalPattern().IsMatch(text)
            || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var d))
        {
            throw ServiceException.Unprocessable($"{name} is not a valid decimal: {text}");
        }
        d = Math.Round(d, scale.Places, MidpointRounding.AwayFromZero);
        if (Math.Abs(d) >= scale.Limit)
        {
            throw ServiceException.Unprocessable($"{name} is out of range: {text}");
        }
        return d;
    }

    /// <summary>A date, YYYY-MM-DD; null when absent. A malformed or impossible date is a 422.</summary>
    public DateOnly? Date(string name)
    {
        if (Get(name) is not { } v)
        {
            return null;
        }
        if (v.ValueKind != JsonValueKind.String)
        {
            throw ServiceException.BadRequest($"{name} must be a date string");
        }
        var s = v.GetString()!.Trim();
        if (s.Length == 0)
        {
            return null;
        }
        return TryParseDate(s) ?? throw ServiceException.Unprocessable($"{name} is not a valid date: {s}");
    }

    public DateOnly ReqDate(string name) => Date(name) ?? throw ServiceException.BadRequest($"{name} is required");

    internal static DateOnly? TryParseDate(string s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>The objects of an array field; empty when absent.</summary>
    public IReadOnlyList<Input> List(string name)
    {
        if (Get(name) is not { } v)
        {
            return [];
        }
        if (v.ValueKind != JsonValueKind.Array)
        {
            throw ServiceException.BadRequest($"{name} must be an array");
        }
        var list = new List<Input>();
        foreach (var e in v.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
            {
                throw ServiceException.BadRequest($"each of {name} must be an object");
            }
            list.Add(new Input(e));
        }
        return list;
    }

    /// <summary>The strings of an array field; empty when absent.</summary>
    public IReadOnlyList<string> Strings(string name)
    {
        if (Get(name) is not { } v)
        {
            return [];
        }
        if (v.ValueKind != JsonValueKind.Array)
        {
            throw ServiceException.BadRequest($"{name} must be an array of strings");
        }
        var list = new List<string>();
        foreach (var e in v.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String)
            {
                throw ServiceException.BadRequest($"{name} must be an array of strings");
            }
            var s = e.GetString()!.Trim();
            if (s.Length > 0)
            {
                list.Add(s);
            }
        }
        return list;
    }

    [GeneratedRegex(@"^[+-]?(\d+(\.\d*)?|\.\d+)$")]
    private static partial Regex DecimalPattern();
}

/// <summary>A stored decimal's scale and the magnitude it must stay below (spec/api.md §1.2).</summary>
public readonly record struct Scale(int Places, decimal Limit)
{
    /// <summary>Money, quantities, unit prices and costs.</summary>
    public static readonly Scale Money = new(4, 1_000_000_000_000_000m);

    /// <summary>Tax rates, in percent.</summary>
    public static readonly Scale Rate = new(4, 1000m);

    /// <summary>Exchange rates.</summary>
    public static readonly Scale Fx = new(8, 100_000_000_000m);
}
