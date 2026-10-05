using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Tadmor.Db;
using Tadmor.Printing;

namespace Tadmor.Services;

/// <summary>How one printable collection is fetched and labeled (spec/api.md §5.11).</summary>
public sealed record PrintKind
{
    public required string Collection { get; init; }
    /// <summary>The title, footer, and PDF document kind.</summary>
    public required string Kind { get; init; }
    /// <summary>The email subject's label.</summary>
    public required string Label { get; init; }
    public required string Prefix { get; init; }
    public required string NumberLabel { get; init; }
    public required string DateLabel { get; init; }
    public string? SecondDateLabel { get; init; }
    public required string UnitLabel { get; init; }
    public required string PartyLabel { get; init; }
    public string AppliedLabel { get; init; } = "";
    public string BalanceLabel { get; init; } = "";
    public required string Table { get; init; }
    public required string NumberColumn { get; init; }
    public required string DateColumn { get; init; }
    public string? SecondDateColumn { get; init; }
    public required string PartyTable { get; init; }
    public required string PartyColumn { get; init; }
    public string? BalanceView { get; init; }
    public string? BalanceId { get; init; }
    public required string LinesTable { get; init; }
    public required string LineDocument { get; init; }
    public required string PriceColumn { get; init; }

    public static readonly PrintKind[] All =
    [
        new()
        {
            Collection = "sales-invoices", Kind = "Invoice", Label = "Invoice", Prefix = "invoice", NumberLabel = "Invoice no.",
            DateLabel = "Invoice date", SecondDateLabel = "Due date", UnitLabel = "UNIT PRICE", PartyLabel = "BILL TO",
            AppliedLabel = "Amount paid", BalanceLabel = "Balance due", Table = "sales_invoices", NumberColumn = "invoice_number",
            DateColumn = "invoice_date", SecondDateColumn = "due_date", PartyTable = "customers", PartyColumn = "customer_id",
            BalanceView = "sales_invoice_balances", BalanceId = "invoice_id", LinesTable = "sales_invoice_lines",
            LineDocument = "invoice_id", PriceColumn = "unit_price",
        },
        new()
        {
            Collection = "purchase-bills", Kind = "Bill", Label = "Bill", Prefix = "bill", NumberLabel = "Bill no.",
            DateLabel = "Bill date", SecondDateLabel = "Due date", UnitLabel = "UNIT COST", PartyLabel = "SUPPLIER",
            AppliedLabel = "Amount paid", BalanceLabel = "Balance due", Table = "purchase_bills", NumberColumn = "bill_number",
            DateColumn = "bill_date", SecondDateColumn = "due_date", PartyTable = "suppliers", PartyColumn = "supplier_id",
            BalanceView = "purchase_bill_balances", BalanceId = "bill_id", LinesTable = "purchase_bill_lines",
            LineDocument = "bill_id", PriceColumn = "unit_cost",
        },
        new()
        {
            Collection = "sales-credit-notes", Kind = "Credit Note", Label = "Credit Note", Prefix = "credit-note",
            NumberLabel = "Credit note no.", DateLabel = "Credit note date", UnitLabel = "UNIT PRICE", PartyLabel = "CREDIT TO",
            AppliedLabel = "Amount applied", BalanceLabel = "Unapplied", Table = "sales_credit_notes",
            NumberColumn = "credit_note_number", DateColumn = "credit_note_date", PartyTable = "customers", PartyColumn = "customer_id",
            BalanceView = "sales_credit_note_balances", BalanceId = "credit_note_id", LinesTable = "sales_credit_note_lines",
            LineDocument = "credit_note_id", PriceColumn = "unit_price",
        },
        new()
        {
            Collection = "purchase-credit-notes", Kind = "Supplier Credit", Label = "Credit Note", Prefix = "supplier-credit",
            NumberLabel = "Credit note no.", DateLabel = "Credit note date", UnitLabel = "UNIT COST", PartyLabel = "SUPPLIER",
            AppliedLabel = "Amount applied", BalanceLabel = "Unapplied", Table = "purchase_credit_notes",
            NumberColumn = "credit_note_number", DateColumn = "credit_note_date", PartyTable = "suppliers", PartyColumn = "supplier_id",
            BalanceView = "purchase_credit_note_balances", BalanceId = "credit_note_id", LinesTable = "purchase_credit_note_lines",
            LineDocument = "credit_note_id", PriceColumn = "unit_cost",
        },
        new()
        {
            Collection = "sales-orders", Kind = "Sales Order", Label = "Sales Order", Prefix = "sales-order", NumberLabel = "Order no.",
            DateLabel = "Order date", SecondDateLabel = "Expected ship", UnitLabel = "UNIT PRICE", PartyLabel = "CUSTOMER",
            Table = "sales_orders", NumberColumn = "order_number", DateColumn = "order_date", SecondDateColumn = "expected_ship_date",
            PartyTable = "customers", PartyColumn = "customer_id", LinesTable = "sales_order_lines", LineDocument = "order_id",
            PriceColumn = "unit_price",
        },
        new()
        {
            Collection = "purchase-orders", Kind = "Purchase Order", Label = "Purchase Order", Prefix = "purchase-order",
            NumberLabel = "Order no.", DateLabel = "Order date", SecondDateLabel = "Expected receipt", UnitLabel = "UNIT COST",
            PartyLabel = "SUPPLIER", Table = "purchase_orders", NumberColumn = "order_number", DateColumn = "order_date",
            SecondDateColumn = "expected_receipt_date", PartyTable = "suppliers", PartyColumn = "supplier_id",
            LinesTable = "purchase_order_lines", LineDocument = "order_id", PriceColumn = "unit_cost",
        },
    ];
}

/// <summary>
/// Printed documents (spec/domain.md §11) and emailing them
/// (spec/api.md §5.11). Every printable document shares one layout.
/// </summary>
public sealed partial class Printer(TadmorDb db, Mailer mailer)
{
    public sealed record Pdf(byte[] Bytes, string FileName);

    public sealed class Header
    {
        public string Number { get; set; } = "";
        public DateOnly Date { get; set; }
        public DateOnly? SecondDate { get; set; }
        public string Currency { get; set; } = "";
        public string Status { get; set; } = "";
        public decimal Subtotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal Total { get; set; }
        public decimal? Applied { get; set; }
        public decimal? Balance { get; set; }
        public string? Reference { get; set; }
        public string? Memo { get; set; }
        public int OrganizationId { get; set; }
        public string Name { get; set; } = "";
        public string? LegalName { get; set; }
        public string? TaxId { get; set; }
        public string? Email { get; set; }
    }

    public sealed class Org
    {
        public string Name { get; set; } = "";
        public string? LegalName { get; set; }
        public string? TaxId { get; set; }
        public string? Line1 { get; set; }
        public string? Line2 { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
    }

    public sealed class LineRow
    {
        public int LineNo { get; set; }
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal Unit { get; set; }
        public decimal TaxRate { get; set; }
        public decimal Subtotal { get; set; }
    }

    private async Task<Header> HeaderAsync(PrintKind k, int id)
    {
        var second = k.SecondDateColumn is null ? "NULL::date" : "d." + k.SecondDateColumn;
        var applied = k.BalanceView is null ? "NULL::numeric" : "b.amount_applied";
        var balance = k.BalanceView is null ? "NULL::numeric" : "b.balance";
        var join = k.BalanceView is null ? "" : $"LEFT JOIN {k.BalanceView} b ON b.{k.BalanceId} = d.id";
        return (await db.ListAsync<Header>($$"""
            SELECT d.{{k.NumberColumn}} AS "Number", d.{{k.DateColumn}} AS "Date", {{second}} AS "SecondDate",
                d.currency_code AS "Currency", d.status AS "Status", d.subtotal AS "Subtotal", d.tax_total AS "TaxTotal",
                d.total AS "Total", {{applied}} AS "Applied", {{balance}} AS "Balance", d.reference AS "Reference", d.memo AS "Memo",
                o.id AS "OrganizationId", o.name AS "Name", o.legal_name AS "LegalName", o.tax_id AS "TaxId", o.email AS "Email"
            FROM {{k.Table}} d
            JOIN {{k.PartyTable}} p ON p.id = d.{{k.PartyColumn}}
            JOIN organizations o ON o.id = p.organization_id
            {{join}}
            WHERE d.id = {0}
            """, id)).SingleOrDefault() ?? throw ServiceException.NotFound();
    }

    private static readonly string OrgSql = """
        SELECT o.name AS "Name", o.legal_name AS "LegalName", o.tax_id AS "TaxId", a.line1 AS "Line1", a.line2 AS "Line2",
            a.city AS "City", a.region AS "Region", a.postal_code AS "PostalCode", co.name AS "Country"
        FROM organizations o
        LEFT JOIN LATERAL (SELECT * FROM addresses WHERE organization_id = o.id ORDER BY id LIMIT 1) a ON true
        LEFT JOIN countries co ON co.code = a.country_code
        """;

    private static PartyBlock Block(Org o) => new(o.Name, o.LegalName, o.TaxId,
        Layout.AddressLines(o.Line1, o.Line2, o.City, o.Region, o.PostalCode, o.Country));

    /// <summary>The document as a PDF, with its download filename; 404 if it does not exist.</summary>
    public async Task<Pdf> PdfAsync(PrintKind k, int id)
    {
        var h = await HeaderAsync(k, id);
        var party = (await db.ListAsync<Org>(OrgSql + " WHERE o.id = {0}", h.OrganizationId)).Single();
        var self = (await db.ListAsync<Org>(OrgSql + " WHERE o.is_self")).SingleOrDefault();
        var lines = await db.ListAsync<LineRow>($$"""
            SELECT line_no AS "LineNo", description AS "Description", quantity AS "Quantity", {{k.PriceColumn}} AS "Unit",
                tax_rate AS "TaxRate", line_subtotal AS "Subtotal"
            FROM {{k.LinesTable}} WHERE {{k.LineDocument}} = {0} ORDER BY line_no
            """, id);

        var meta = new List<(string, string)> { (k.NumberLabel, h.Number), (k.DateLabel, h.Date.ToString("yyyy-MM-dd")) };
        if (k.SecondDateLabel is not null && h.SecondDate is { } second)
        {
            meta.Add((k.SecondDateLabel, second.ToString("yyyy-MM-dd")));
        }
        meta.Add(("Currency", h.Currency));
        var doc = new PrintDoc
        {
            Kind = k.Kind, Number = h.Number, Status = h.Status, Currency = h.Currency, Meta = meta, PartyLabel = k.PartyLabel,
            Party = Block(party) with { Name = h.Name }, Seller = self is null ? null : Block(self), UnitLabel = k.UnitLabel,
            Subtotal = h.Subtotal, TaxTotal = h.TaxTotal, Total = h.Total, Applied = h.Applied ?? 0, Balance = h.Balance ?? 0,
            AppliedLabel = k.AppliedLabel, BalanceLabel = k.BalanceLabel, Reference = h.Reference, Memo = h.Memo,
            Lines = lines.Select(l => new PrintLine(l.LineNo, l.Description, l.Quantity, l.Unit, l.TaxRate, l.Subtotal)).ToList(),
        };
        return new Pdf(Layout.Render(doc), $"{k.Prefix}-{UnsafeFileChars().Replace(h.Number, "-")}.pdf");
    }

    /// <summary>
    /// Emails the document's PDF. With no recipients given, the
    /// counterparty organization's email is used (422 if it has none).
    /// When sending is disabled the request is refused with 501.
    /// </summary>
    public async Task<IReadOnlyList<string>> EmailAsync(PrintKind k, int id, Input input)
    {
        var to = input.Strings("to");
        var h = await HeaderAsync(k, id);
        if (to.Count == 0)
        {
            to = h.Email is { Length: > 0 } email
                ? [email]
                : throw ServiceException.Unprocessable($"no recipient: {h.Name} has no email on file");
        }
        if (!mailer.Enabled)
        {
            throw new ServiceException(501, "email sending is not configured on this server");
        }
        var pdf = await PdfAsync(k, id);
        await mailer.SendAsync(to, $"{k.Label} {h.Number}", $"Please find {k.Label.ToLowerInvariant()} {h.Number} attached.",
            pdf.FileName, pdf.Bytes);
        return to;
    }

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeFileChars();
}

/// <summary>
/// Sends email through SMTP_ADDR (host:port) with the base class library's
/// SmtpClient, authenticating with SMTP_USER and SMTP_PASS when set, from
/// MAIL_FROM. With SMTP_ADDR unset, sending is disabled.
/// </summary>
public sealed class Mailer
{
    private readonly string? host;
    private readonly int port;
    private readonly string? user;
    private readonly string? password;
    private readonly string from;

    public Mailer()
    {
        var addr = Environment.GetEnvironmentVariable("SMTP_ADDR")?.Trim();
        if (!string.IsNullOrEmpty(addr))
        {
            var colon = addr.LastIndexOf(':');
            host = colon < 0 ? addr : addr[..colon];
            port = colon < 0 ? 25 : int.Parse(addr[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        }
        user = Environment.GetEnvironmentVariable("SMTP_USER");
        password = Environment.GetEnvironmentVariable("SMTP_PASS");
        from = Environment.GetEnvironmentVariable("MAIL_FROM") ?? "tadmor@localhost";
    }

    public bool Enabled => host is not null;

    public async Task SendAsync(IReadOnlyList<string> to, string subject, string body, string fileName, byte[] attachment)
    {
        using var message = new MailMessage { From = new MailAddress(from), Subject = subject, Body = body };
        foreach (var address in to)
        {
            message.To.Add(address);
        }
        message.Attachments.Add(new Attachment(new MemoryStream(attachment), fileName, "application/pdf"));
        using var client = new SmtpClient(host, port);
        if (!string.IsNullOrEmpty(user))
        {
            // Credentials go only over TLS (STARTTLS).
            client.EnableSsl = true;
            client.Credentials = new NetworkCredential(user, password);
        }
        await client.SendMailAsync(message);
    }
}
