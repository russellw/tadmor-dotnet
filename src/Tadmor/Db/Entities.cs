namespace Tadmor.Db;

// Hand-written entities over the shared schema (docs/stack.md). Property
// names are the columns' in PascalCase; TadmorDb maps them to snake_case.
// Columns the database maintains (identity keys, generated amounts, header
// totals kept by trigger) are read-only to EF Core, and audit columns the
// application never reads are not mapped.

public sealed class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsAdmin { get; set; }
}

public sealed class Session
{
    public required byte[] TokenHash { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class Organization
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? CountryCode { get; set; }
    public string? DefaultCurrency { get; set; }
    public string? Email { get; set; }
    public bool IsSelf { get; set; }
}

public sealed class Address
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string? Label { get; set; }
    public required string Line1 { get; set; }
    public string? Line2 { get; set; }
    public required string City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public required string CountryCode { get; set; }
}

public sealed class Account
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string AccountType { get; set; }
    public int? ParentId { get; set; }
    public string? CurrencyCode { get; set; }
    public bool IsPostable { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsCash { get; set; }
    public string CashFlowActivity { get; set; } = "operating";
}

public sealed class TaxCode
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public decimal Rate { get; set; }
    public int? TaxAccountId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PaymentTerm
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int DueDays { get; set; }
}

public sealed class Warehouse
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int? AddressId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Product
{
    public int Id { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal UnitPrice { get; set; }
    public string? CurrencyCode { get; set; }
    public int? RevenueAccountId { get; set; }
    public string? TaxCode { get; set; }
    public bool TrackInventory { get; set; }
    public int? InventoryAccountId { get; set; }
    public int? CogsAccountId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A customer or supplier: a role on an organization.</summary>
public abstract class Party
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string? Number { get; set; }
    /// <summary>The A/R (customers) or A/P (suppliers) control account.</summary>
    public int? ControlAccountId { get; set; }
    public string? PaymentTermsCode { get; set; }
    public string? CurrencyCode { get; set; }
    public string? TaxCode { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Customer : Party
{
    public decimal? CreditLimit { get; set; }
}

public sealed class Supplier : Party;

public sealed class FiscalYear
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = "open";
    public int? ClosingEntryId { get; set; }
}

public sealed class AccountingPeriod
{
    public int Id { get; set; }
    public int FiscalYearId { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = "open";
}

public sealed class JournalEntry
{
    public int Id { get; set; }
    public DateOnly EntryDate { get; set; }
    public int PeriodId { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; } = 1;
    public string? Reference { get; set; }
    public string? Memo { get; set; }
    public string Status { get; set; } = "draft";
    public DateTimeOffset? PostedAt { get; set; }
    public int? ReversesEntryId { get; set; }
    public bool IsClosing { get; set; }
    public List<JournalLine> Lines { get; set; } = [];
}

public sealed class JournalLine
{
    public int Id { get; set; }
    public int JournalEntryId { get; set; }
    public int LineNo { get; set; }
    public int AccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal BaseDebit { get; set; }
    public decimal BaseCredit { get; set; }
    public string? Memo { get; set; }
}

public sealed class GlSettings
{
    public int Id { get; set; } = 1;
    public required string BaseCurrency { get; set; }
    public int? FxGainLossAccountId { get; set; }
}

public sealed class ExchangeRate
{
    public required string CurrencyCode { get; set; }
    public DateOnly RateDate { get; set; }
    public decimal Rate { get; set; }
}

public sealed class Currency
{
    public required string Code { get; set; }
}

public sealed class Country
{
    public required string Code { get; set; }
    public required string Name { get; set; }
}

/// <summary>
/// The header shared by invoices, bills, and both kinds of credit note
/// (spec/api.md §5.9). Each kind maps it onto its own table and column
/// names; DueDate is unmapped for credit notes.
/// </summary>
public abstract class LineDocument
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int PartyId { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public string CurrencyCode { get; set; } = "";
    public string Status { get; set; } = "draft";
    public int? PeriodId { get; set; }
    public int? JournalEntryId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public string? Reference { get; set; }
    public string? Memo { get; set; }
}

public sealed class SalesInvoice : LineDocument;
public sealed class PurchaseBill : LineDocument;
public sealed class SalesCreditNote : LineDocument;
public sealed class PurchaseCreditNote : LineDocument;

/// <summary>
/// A line of a line document or an order. Price is unit_price on the sales
/// side and unit_cost on the purchase side; AccountId is the revenue or
/// expense account. The amounts are generated by the database.
/// </summary>
public abstract class DocumentLine
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int LineNo { get; set; }
    public int? ProductId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public int? AccountId { get; set; }
    public string? TaxCode { get; set; }
    public decimal TaxRate { get; set; }
    public decimal LineSubtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    /// <summary>The order line drawn on (invoice and bill lines only).</summary>
    public int? OrderLineId { get; set; }
}

public sealed class SalesInvoiceLine : DocumentLine;
public sealed class PurchaseBillLine : DocumentLine;
public sealed class SalesCreditNoteLine : DocumentLine;
public sealed class PurchaseCreditNoteLine : DocumentLine;
public sealed class SalesOrderLine : DocumentLine;
public sealed class PurchaseOrderLine : DocumentLine;

/// <summary>A customer or supplier payment.</summary>
public abstract class Payment
{
    public int Id { get; set; }
    public int PartyId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Method { get; set; }
    public string? Reference { get; set; }
    public string Status { get; set; } = "draft";
    public int? PeriodId { get; set; }
    public int? JournalEntryId { get; set; }
    /// <summary>The deposit (customers) or payment (suppliers) account.</summary>
    public int? CashAccountId { get; set; }
}

public sealed class CustomerPayment : Payment;
public sealed class SupplierPayment : Payment;

/// <summary>
/// An allocation of a payment or credit note (the settler) to an invoice or
/// bill (the document), with the realized-FX entry it posted, if any.
/// </summary>
public abstract class Application
{
    public int Id { get; set; }
    public int SettlerId { get; set; }
    public int DocumentId { get; set; }
    public decimal AmountApplied { get; set; }
    public int? FxJournalEntryId { get; set; }
}

public sealed class PaymentApplication : Application;
public sealed class BillApplication : Application;
public sealed class SalesCreditApplication : Application;
public sealed class PurchaseCreditApplication : Application;

/// <summary>A sales or purchase order header (spec/api.md §5.10).</summary>
public abstract class Order
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public int PartyId { get; set; }
    public DateOnly OrderDate { get; set; }
    /// <summary>The expected ship (sales) or receipt (purchases) date.</summary>
    public DateOnly? ExpectedDate { get; set; }
    public string CurrencyCode { get; set; } = "";
    public string Status { get; set; } = "draft";
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public string? Reference { get; set; }
    public string? Memo { get; set; }
}

public sealed class SalesOrder : Order;
public sealed class PurchaseOrder : Order;

public sealed class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public DateOnly MovementDate { get; set; }
    public required string MovementType { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? SourceType { get; set; }
    public int? SourceId { get; set; }
    public int? PeriodId { get; set; }
    public int? JournalEntryId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public sealed class BankStatement
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public DateOnly StatementDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public string? Reference { get; set; }
    public string Status { get; set; } = "open";
    public DateTimeOffset? ReconciledAt { get; set; }
}

public sealed class BankStatementLine
{
    public int Id { get; set; }
    public int StatementId { get; set; }
    public int LineNo { get; set; }
    public DateOnly TxnDate { get; set; }
    public required string Description { get; set; }
    public string? Reference { get; set; }
    public decimal Amount { get; set; }
    public int? JournalLineId { get; set; }
}

// ---- views (keyless) ----

/// <summary>A row of one of the *_balances views: what is applied, and what remains.</summary>
public abstract class DocumentBalance
{
    public int DocumentId { get; set; }
    public decimal AmountApplied { get; set; }
    public decimal Balance { get; set; }
    /// <summary>payment_status (invoices, bills) or application_status (credit notes).</summary>
    public required string SettlementStatus { get; set; }
}

public sealed class SalesInvoiceBalance : DocumentBalance;
public sealed class PurchaseBillBalance : DocumentBalance;
public sealed class SalesCreditNoteBalance : DocumentBalance;
public sealed class PurchaseCreditNoteBalance : DocumentBalance;

/// <summary>A row of sales_ or purchase_order_line_fulfilment.</summary>
public abstract class OrderLineFulfilment
{
    public int OrderLineId { get; set; }
    public int OrderId { get; set; }
    /// <summary>Invoiced (sales) or billed (purchases).</summary>
    public decimal QtyCharged { get; set; }
    /// <summary>Shipped (sales) or received (purchases).</summary>
    public decimal QtyMoved { get; set; }
    public decimal QtyToCharge { get; set; }
    public decimal QtyToMove { get; set; }
}

public sealed class SalesOrderLineFulfilment : OrderLineFulfilment;
public sealed class PurchaseOrderLineFulfilment : OrderLineFulfilment;

/// <summary>A row of sales_ or purchase_order_fulfilment.</summary>
public abstract class OrderFulfilment
{
    public int OrderId { get; set; }
    public required string ChargedStatus { get; set; }
    public required string MovedStatus { get; set; }
}

public sealed class SalesOrderFulfilment : OrderFulfilment;
public sealed class PurchaseOrderFulfilment : OrderFulfilment;

public sealed class StockValuation
{
    public int ProductId { get; set; }
    public decimal QtyOnHand { get; set; }
    public decimal ValueOnHand { get; set; }
    public decimal AvgUnitCost { get; set; }
}

/// <summary>A row of ar_aging or ap_aging.</summary>
public abstract class Aging
{
    public int PartyId { get; set; }
    public decimal TotalOutstanding { get; set; }
    public decimal? NotYetDue { get; set; }
    public decimal? Days130 { get; set; }
    public decimal? Days3160 { get; set; }
    public decimal? Days6190 { get; set; }
    public decimal? DaysOver90 { get; set; }
}

public sealed class ArAging : Aging;
public sealed class ApAging : Aging;
