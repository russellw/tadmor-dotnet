using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Primitives;
using Tadmor.Services;

namespace Tadmor.Ui;

/// <summary>
/// Turns a posted form into the same Input the JSON API reads, so a form
/// and an API request are checked by the same rules. Fields are named as in
/// the API. By convention a field ending in _id, and due_days, is an
/// integer; is_* and track_inventory are checkboxes; the rest are strings
/// (decimals and dates included, as in the API). Line fields are named
/// line_&lt;field&gt;, repeated once per row; rows left blank are dropped.
/// </summary>
public static class FormInput
{
    private const string LinePrefix = "line_";

    public static Input From(IFormCollection form, params string[] arrays)
    {
        var obj = new JsonObject();
        foreach (var (key, values) in form)
        {
            if (key.StartsWith("__", StringComparison.Ordinal) || key.StartsWith(LinePrefix, StringComparison.Ordinal))
            {
                continue;
            }
            if (arrays.Contains(key))
            {
                var list = new JsonArray();
                foreach (var s in values.ToString().Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    list.Add(s.Trim());
                }
                obj[key] = list;
                continue;
            }
            if (Value(key, values.ToString()) is { } node)
            {
                obj[key] = node;
            }
        }
        var lines = Lines(form);
        if (lines is not null)
        {
            obj["lines"] = lines;
        }
        return Input.From(JsonSerializer.SerializeToElement(obj));
    }

    private static JsonNode? Value(string key, string value)
    {
        value = value.Trim();
        if (IsFlag(key))
        {
            return JsonValue.Create(value is "true" or "on");
        }
        if (value.Length == 0)
        {
            return null;
        }
        if ((key.EndsWith("_id", StringComparison.Ordinal) || key == "due_days") && long.TryParse(value, out var n))
        {
            return JsonValue.Create(n);
        }
        return JsonValue.Create(value);
    }

    private static bool IsFlag(string key) => key.StartsWith("is_", StringComparison.Ordinal) || key == "track_inventory";

    /// <summary>The line rows, or null when the form has no line fields.</summary>
    private static JsonArray? Lines(IFormCollection form)
    {
        var fields = form.Keys.Where(k => k.StartsWith(LinePrefix, StringComparison.Ordinal)).ToList();
        if (fields.Count == 0)
        {
            return null;
        }
        var rows = fields.Max(k => form[k].Count);
        var lines = new JsonArray();
        for (var i = 0; i < rows; i++)
        {
            var line = new JsonObject();
            foreach (var k in fields)
            {
                StringValues values = form[k];
                var v = i < values.Count ? values[i] ?? "" : "";
                if (Value(k[LinePrefix.Length..], v) is { } node)
                {
                    line[k[LinePrefix.Length..]] = node;
                }
            }
            // A row with neither a description nor a product was left blank.
            if (line.ContainsKey("description") || line.ContainsKey("product_id") || line.ContainsKey("order_line_id"))
            {
                lines.Add(line);
            }
        }
        return lines;
    }
}
