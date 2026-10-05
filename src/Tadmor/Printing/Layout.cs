using System.Globalization;
using System.Text;
using static Tadmor.Printing.Pdf;

namespace Tadmor.Printing;

/// <summary>One side of a document: an organization and its address.</summary>
internal sealed record PartyBlock(string Name, string? Legal, string? TaxId, IReadOnlyList<string> Address);

/// <summary>One row of the line table.</summary>
internal sealed record PrintLine(int No, string Description, decimal Quantity, decimal Unit, decimal TaxRate, decimal Subtotal);

/// <summary>
/// Everything the layout needs for one document. An empty AppliedLabel
/// means the document has no applications (orders), so the applied and
/// balance rows never render.
/// </summary>
internal sealed record PrintDoc
{
    public required string Kind { get; init; }
    public required string Number { get; init; }
    public required string Status { get; init; }
    public required string Currency { get; init; }
    public required IReadOnlyList<(string Label, string Value)> Meta { get; init; }
    public required string PartyLabel { get; init; }
    public required PartyBlock Party { get; init; }
    public PartyBlock? Seller { get; init; }
    public required string UnitLabel { get; init; }
    public decimal Subtotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal Total { get; init; }
    public decimal Applied { get; init; }
    public decimal Balance { get; init; }
    public string AppliedLabel { get; init; } = "";
    public string BalanceLabel { get; init; } = "";
    public string? Reference { get; init; }
    public string? Memo { get; init; }
    public required IReadOnlyList<PrintLine> Lines { get; init; }
}

/// <summary>
/// The one layout every printable document shares (spec/domain.md §11),
/// ported from tadmor's internal/printing/render.go: A4, Helvetica, a title
/// and meta block, the issuer and counterparty, the line table (continued
/// onto further pages with its header repeated), totals, reference and memo,
/// and a page footer.
/// </summary>
internal static class Layout
{
    private const double PageW = 595.28, PageH = 841.89;
    private const double MarginL = 54, MarginR = 54, RightX = PageW - MarginR, TopY = PageH - 54, BottomY = 72;
    private const double NumX = MarginL, DescX = MarginL + 26, QtyX = 360, PriceX = 432, TaxX = 472, AmountX = RightX;
    private const double DescMax = QtyX - 60 - DescX;
    private const double Gray = 0.45;

    private static string Title(PrintDoc d)
    {
        var t = d.Kind.ToUpperInvariant();
        return d.Status switch
        {
            "draft" => "DRAFT " + t,
            "void" => "VOID " + t,
            "cancelled" => "CANCELLED " + t,
            _ => t,
        };
    }

    public static byte[] Render(PrintDoc d)
    {
        var doc = new Pdf();
        var p = doc.AddPage(PageW, PageH);

        var title = Title(d);
        p.Text(Font.HelveticaBold, 20, RightX - Width(Font.HelveticaBold, 20, title), TopY - 6, title);
        var metaY = TopY - 36;
        foreach (var (label, value) in d.Meta)
        {
            p.TextGray(Font.Helvetica, 9, RightX - 140, metaY, Gray, label);
            p.Text(Font.Helvetica, 9, RightX - Width(Font.Helvetica, 9, value), metaY, value);
            metaY -= 13;
        }

        var y = TopY - 6;
        if (d.Seller is not null)
        {
            y = PartyText(p, MarginL, y, 11, d.Seller);
        }
        y = Math.Min(y, metaY) - 28;
        p.TextGray(Font.HelveticaBold, 8, MarginL, y, Gray, d.PartyLabel);
        y -= 14;
        y = PartyText(p, MarginL, y, 10, d.Party);

        y -= 24;
        y = TableHeader(p, y, d.UnitLabel);
        foreach (var l in d.Lines)
        {
            if (y < BottomY + 20)
            {
                p = doc.AddPage(PageW, PageH);
                y = TableHeader(p, TopY, d.UnitLabel);
            }
            p.TextGray(Font.Helvetica, 9, NumX, y, Gray, l.No.ToString(CultureInfo.InvariantCulture));
            p.Text(Font.Helvetica, 9, DescX, y, Truncate(9, DescMax, l.Description));
            foreach (var (x, s) in new[] { (QtyX, Qty(l.Quantity)), (PriceX, Amount(l.Unit)), (TaxX, Qty(l.TaxRate)), (AmountX, Amount(l.Subtotal)) })
            {
                p.Text(Font.Helvetica, 9, x - Width(Font.Helvetica, 9, s), y, s);
            }
            y -= 6;
            p.Line(MarginL, y, RightX, y, 0.4, 0.9);
            y -= 12;
        }

        if (y < BottomY + 110)
        {
            p = doc.AddPage(PageW, PageH);
            y = TopY;
        }
        y -= 8;
        const double totalsX = 400;
        void Total(string label, string value, Font f)
        {
            p.Text(f, 9, totalsX, y, label);
            p.Text(f, 9, RightX - Width(f, 9, value), y, value);
            y -= 14;
        }
        Total("Subtotal", Amount(d.Subtotal), Font.Helvetica);
        Total("Tax", Amount(d.TaxTotal), Font.Helvetica);
        p.Line(totalsX, y + 9, RightX, y + 9, 0.8, 0.2);
        y -= 2;
        Total("Total", d.Currency + " " + Amount(d.Total), Font.HelveticaBold);
        if (d.AppliedLabel != "" && d.Applied != 0)
        {
            Total(d.AppliedLabel, Amount(d.Applied), Font.Helvetica);
            Total(d.BalanceLabel, d.Currency + " " + Amount(d.Balance), Font.HelveticaBold);
        }

        var noteY = y - 14;
        if (!string.IsNullOrEmpty(d.Reference))
        {
            p.TextGray(Font.Helvetica, 9, MarginL, noteY, Gray, "Reference: " + d.Reference);
            noteY -= 13;
        }
        if (!string.IsNullOrEmpty(d.Memo))
        {
            foreach (var line in Wrap(9, RightX - MarginL, d.Memo))
            {
                p.TextGray(Font.Helvetica, 9, MarginL, noteY, Gray, line);
                noteY -= 13;
            }
        }

        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var footer = $"{d.Kind} {d.Number}  ·  Page {i + 1} of {doc.Pages.Count}";
            doc.Pages[i].TextGray(Font.Helvetica, 8, (PageW - Width(Font.Helvetica, 8, footer)) / 2, 40, Gray, footer);
        }
        return doc.Bytes();
    }

    private static double PartyText(Page p, double x, double y, double nameSize, PartyBlock b)
    {
        p.Text(Font.HelveticaBold, nameSize, x, y, b.Name);
        y -= 13;
        if (!string.IsNullOrEmpty(b.Legal) && b.Legal != b.Name)
        {
            p.TextGray(Font.Helvetica, 9, x, y, Gray, b.Legal);
            y -= 12;
        }
        foreach (var line in b.Address)
        {
            p.TextGray(Font.Helvetica, 9, x, y, Gray, line);
            y -= 12;
        }
        if (!string.IsNullOrEmpty(b.TaxId))
        {
            p.TextGray(Font.Helvetica, 9, x, y, Gray, "Tax ID: " + b.TaxId);
            y -= 12;
        }
        return y;
    }

    private static double TableHeader(Page p, double y, string unitLabel)
    {
        void H(double x, string label, bool right) =>
            p.TextGray(Font.HelveticaBold, 8, right ? x - Width(Font.HelveticaBold, 8, label) : x, y, Gray, label);
        H(NumX, "#", false);
        H(DescX, "DESCRIPTION", false);
        H(QtyX, "QTY", true);
        H(PriceX, unitLabel, true);
        H(TaxX, "TAX %", true);
        H(AmountX, "AMOUNT", true);
        y -= 6;
        p.Line(MarginL, y, RightX, y, 0.8, 0.2);
        return y - 14;
    }

    /// <summary>Formats a postal address as display lines, skipping blanks.</summary>
    public static List<string> AddressLines(string? line1, string? line2, string? city, string? region, string? postal, string? country)
    {
        var lines = new List<string>();
        foreach (var l in new[] { line1, line2 })
        {
            if (!string.IsNullOrEmpty(l))
            {
                lines.Add(l);
            }
        }
        var cityLine = city ?? "";
        if (!string.IsNullOrEmpty(region))
        {
            cityLine += ", " + region;
        }
        if (!string.IsNullOrEmpty(postal))
        {
            cityLine += " " + postal;
        }
        cityLine = cityLine.StartsWith(", ", StringComparison.Ordinal) ? cityLine[2..] : cityLine;
        if (cityLine != "")
        {
            lines.Add(cityLine);
        }
        if (!string.IsNullOrEmpty(country))
        {
            lines.Add(country);
        }
        return lines;
    }

    private static string Truncate(double size, double max, string s)
    {
        if (Width(Font.Helvetica, size, s) <= max)
        {
            return s;
        }
        while (s.Length > 0 && Width(Font.Helvetica, size, s + "…") > max)
        {
            s = s[..^1];
        }
        return s + "…";
    }

    private static List<string> Wrap(double size, double max, string s)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line == "" ? word : line + " " + word;
            if (Width(Font.Helvetica, size, candidate) > max && line != "")
            {
                lines.Add(line);
                line = word;
                continue;
            }
            line = candidate;
        }
        if (line != "")
        {
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>"1234.5000" as "1,234.50": at least two fraction digits, more only when significant.</summary>
    public static string Amount(decimal d)
    {
        var s = d.ToString(CultureInfo.InvariantCulture);
        var sign = "";
        if (s.StartsWith('-'))
        {
            sign = "-";
            s = s[1..];
        }
        var dot = s.IndexOf('.');
        var intPart = dot < 0 ? s : s[..dot];
        var frac = dot < 0 ? "" : s[(dot + 1)..].TrimEnd('0');
        frac = frac.PadRight(2, '0');
        var grouped = new StringBuilder();
        for (var i = 0; i < intPart.Length; i++)
        {
            if (i > 0 && (intPart.Length - i) % 3 == 0)
            {
                grouped.Append(',');
            }
            grouped.Append(intPart[i]);
        }
        return sign + grouped + "." + frac;
    }

    /// <summary>A quantity without insignificant fraction digits: "2.0000" as "2".</summary>
    public static string Qty(decimal d)
    {
        var s = d.ToString(CultureInfo.InvariantCulture);
        return s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
    }
}
