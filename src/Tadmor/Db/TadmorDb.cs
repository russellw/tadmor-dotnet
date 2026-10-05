using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tadmor.Db;

/// <summary>
/// The EF Core view of the shared schema. Entities are written by hand
/// (docs/stack.md) and map the tables and views as they are; EF Core never
/// creates or migrates anything.
/// </summary>
public sealed class TadmorDb(DbContextOptions<TadmorDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();
    public DbSet<PaymentTerm> PaymentTerms => Set<PaymentTerm>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<GlSettings> GlSettings => Set<GlSettings>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<BankStatement> BankStatements => Set<BankStatement>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    public DbSet<StockValuation> StockValuation => Set<StockValuation>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.ToTable("users");
            // citext, so that parameters compared with it are sent as citext;
            // as text, the comparison would be case-sensitive.
            e.Property(u => u.Email).HasColumnType("citext");
        });
        model.Entity<Session>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(s => s.TokenHash);
        });
        model.Entity<Organization>().ToTable("organizations");
        model.Entity<Address>().ToTable("addresses");
        model.Entity<Account>().ToTable("accounts");
        model.Entity<TaxCode>(e =>
        {
            e.ToTable("tax_codes");
            e.HasKey(t => t.Code);
        });
        model.Entity<PaymentTerm>(e =>
        {
            e.ToTable("payment_terms");
            e.HasKey(t => t.Code);
        });
        model.Entity<Warehouse>().ToTable("warehouses");
        model.Entity<Product>().ToTable("products");
        model.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.Property(c => c.Number).HasColumnName("customer_number");
            e.Property(c => c.ControlAccountId).HasColumnName("ar_account_id");
        });
        model.Entity<Supplier>(e =>
        {
            e.ToTable("suppliers");
            e.Property(c => c.Number).HasColumnName("supplier_number");
            e.Property(c => c.ControlAccountId).HasColumnName("ap_account_id");
        });
        model.Entity<FiscalYear>().ToTable("fiscal_years");
        model.Entity<AccountingPeriod>().ToTable("accounting_periods");
        model.Entity<JournalEntry>(e =>
        {
            e.ToTable("journal_entries");
            e.HasMany(j => j.Lines).WithOne().HasForeignKey(l => l.JournalEntryId);
        });
        model.Entity<JournalLine>().ToTable("journal_lines");
        model.Entity<GlSettings>(e =>
        {
            e.ToTable("gl_settings");
            e.Property(s => s.Id).ValueGeneratedNever();
        });
        model.Entity<ExchangeRate>(e =>
        {
            e.ToTable("exchange_rates");
            e.HasKey(r => new { r.CurrencyCode, r.RateDate });
        });
        model.Entity<Currency>(e =>
        {
            e.ToTable("currencies");
            e.HasKey(c => c.Code);
        });
        model.Entity<Country>(e =>
        {
            e.ToTable("countries");
            e.HasKey(c => c.Code);
        });

        Document<SalesInvoice>(model, "sales_invoices", "invoice_number", "customer_id", "invoice_date", dueDate: true);
        Document<PurchaseBill>(model, "purchase_bills", "bill_number", "supplier_id", "bill_date", dueDate: true);
        Document<SalesCreditNote>(model, "sales_credit_notes", "credit_note_number", "customer_id", "credit_note_date", dueDate: false);
        Document<PurchaseCreditNote>(model, "purchase_credit_notes", "credit_note_number", "supplier_id", "credit_note_date", dueDate: false);

        Lines<SalesInvoiceLine>(model, "sales_invoice_lines", "invoice_id", "unit_price", "revenue_account_id", orderLink: true);
        Lines<PurchaseBillLine>(model, "purchase_bill_lines", "bill_id", "unit_cost", "expense_account_id", orderLink: true);
        Lines<SalesCreditNoteLine>(model, "sales_credit_note_lines", "credit_note_id", "unit_price", "revenue_account_id", orderLink: false);
        Lines<PurchaseCreditNoteLine>(model, "purchase_credit_note_lines", "credit_note_id", "unit_cost", "expense_account_id", orderLink: false);
        Lines<SalesOrderLine>(model, "sales_order_lines", "order_id", "unit_price", "revenue_account_id", orderLink: false);
        Lines<PurchaseOrderLine>(model, "purchase_order_lines", "order_id", "unit_cost", "expense_account_id", orderLink: false);

        PaymentTable<CustomerPayment>(model, "customer_payments", "customer_id", "deposit_account_id");
        PaymentTable<SupplierPayment>(model, "supplier_payments", "supplier_id", "payment_account_id");

        ApplicationTable<PaymentApplication>(model, "payment_applications", "payment_id", "invoice_id");
        ApplicationTable<BillApplication>(model, "bill_applications", "payment_id", "bill_id");
        ApplicationTable<SalesCreditApplication>(model, "sales_credit_applications", "credit_note_id", "invoice_id");
        ApplicationTable<PurchaseCreditApplication>(model, "purchase_credit_applications", "credit_note_id", "bill_id");

        OrderTable<SalesOrder>(model, "sales_orders", "customer_id", "expected_ship_date");
        OrderTable<PurchaseOrder>(model, "purchase_orders", "supplier_id", "expected_receipt_date");

        model.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements");
            Generated(e.Property(m => m.TotalCost));
        });
        model.Entity<BankStatement>().ToTable("bank_statements");
        model.Entity<BankStatementLine>().ToTable("bank_statement_lines");

        BalanceView<SalesInvoiceBalance>(model, "sales_invoice_balances", "invoice_id", "payment_status");
        BalanceView<PurchaseBillBalance>(model, "purchase_bill_balances", "bill_id", "payment_status");
        BalanceView<SalesCreditNoteBalance>(model, "sales_credit_note_balances", "credit_note_id", "application_status");
        BalanceView<PurchaseCreditNoteBalance>(model, "purchase_credit_note_balances", "credit_note_id", "application_status");

        LineFulfilmentView<SalesOrderLineFulfilment>(model, "sales_order_line_fulfilment", "qty_invoiced", "qty_shipped", "qty_to_invoice", "qty_to_ship");
        LineFulfilmentView<PurchaseOrderLineFulfilment>(model, "purchase_order_line_fulfilment", "qty_billed", "qty_received", "qty_to_bill", "qty_to_receive");
        FulfilmentView<SalesOrderFulfilment>(model, "sales_order_fulfilment", "invoiced_status", "shipped_status");
        FulfilmentView<PurchaseOrderFulfilment>(model, "purchase_order_fulfilment", "billed_status", "received_status");

        model.Entity<StockValuation>(e =>
        {
            e.HasNoKey();
            e.ToView("stock_valuation");
        });
        AgingView<ArAging>(model, "ar_aging", "customer_id");
        AgingView<ApAging>(model, "ap_aging", "supplier_id");

        // The schema's names are snake_case; the entities' are PascalCase.
        // Explicit column names above are kept.
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.GetColumnName() == property.Name)
                {
                    property.SetColumnName(SnakeCase(property.Name));
                }
            }
        }
    }

    private static void Document<T>(ModelBuilder model, string table, string number, string party, string date, bool dueDate)
        where T : LineDocument
    {
        model.Entity<T>(e =>
        {
            e.ToTable(table);
            e.Property(d => d.Number).HasColumnName(number);
            e.Property(d => d.PartyId).HasColumnName(party);
            e.Property(d => d.Date).HasColumnName(date);
            if (!dueDate)
            {
                e.Ignore(d => d.DueDate);
            }
            Generated(e.Property(d => d.Subtotal));
            Generated(e.Property(d => d.TaxTotal));
            Generated(e.Property(d => d.Total));
        });
    }

    private static void Lines<T>(ModelBuilder model, string table, string document, string price, string account, bool orderLink)
        where T : DocumentLine
    {
        model.Entity<T>(e =>
        {
            e.ToTable(table);
            e.Property(l => l.DocumentId).HasColumnName(document);
            e.Property(l => l.Price).HasColumnName(price);
            e.Property(l => l.AccountId).HasColumnName(account);
            if (!orderLink)
            {
                e.Ignore(l => l.OrderLineId);
            }
            Generated(e.Property(l => l.LineSubtotal));
            Generated(e.Property(l => l.TaxAmount));
            Generated(e.Property(l => l.LineTotal));
        });
    }

    private static void PaymentTable<T>(ModelBuilder model, string table, string party, string cashAccount) where T : Payment
    {
        model.Entity<T>(e =>
        {
            e.ToTable(table);
            e.Property(p => p.PartyId).HasColumnName(party);
            e.Property(p => p.CashAccountId).HasColumnName(cashAccount);
        });
    }

    private static void ApplicationTable<T>(ModelBuilder model, string table, string settler, string document) where T : Application
    {
        model.Entity<T>(e =>
        {
            e.ToTable(table);
            e.Property(a => a.SettlerId).HasColumnName(settler);
            e.Property(a => a.DocumentId).HasColumnName(document);
        });
    }

    private static void OrderTable<T>(ModelBuilder model, string table, string party, string expected) where T : Order
    {
        model.Entity<T>(e =>
        {
            e.ToTable(table);
            e.Property(o => o.PartyId).HasColumnName(party);
            e.Property(o => o.ExpectedDate).HasColumnName(expected);
            Generated(e.Property(o => o.Subtotal));
            Generated(e.Property(o => o.TaxTotal));
            Generated(e.Property(o => o.Total));
        });
    }

    private static void BalanceView<T>(ModelBuilder model, string view, string id, string status) where T : DocumentBalance
    {
        model.Entity<T>(e =>
        {
            e.HasNoKey();
            e.ToView(view);
            e.Property(b => b.DocumentId).HasColumnName(id);
            e.Property(b => b.SettlementStatus).HasColumnName(status);
        });
    }

    private static void LineFulfilmentView<T>(ModelBuilder model, string view, string charged, string moved, string toCharge, string toMove)
        where T : OrderLineFulfilment
    {
        model.Entity<T>(e =>
        {
            e.HasNoKey();
            e.ToView(view);
            e.Property(f => f.QtyCharged).HasColumnName(charged);
            e.Property(f => f.QtyMoved).HasColumnName(moved);
            e.Property(f => f.QtyToCharge).HasColumnName(toCharge);
            e.Property(f => f.QtyToMove).HasColumnName(toMove);
        });
    }

    private static void FulfilmentView<T>(ModelBuilder model, string view, string charged, string moved) where T : OrderFulfilment
    {
        model.Entity<T>(e =>
        {
            e.HasNoKey();
            e.ToView(view);
            e.Property(f => f.ChargedStatus).HasColumnName(charged);
            e.Property(f => f.MovedStatus).HasColumnName(moved);
        });
    }

    private static void AgingView<T>(ModelBuilder model, string view, string party) where T : Aging
    {
        model.Entity<T>(e =>
        {
            e.HasNoKey();
            e.ToView(view);
            e.Property(a => a.PartyId).HasColumnName(party);
            e.Property(a => a.Days130).HasColumnName("days_1_30");
            e.Property(a => a.Days3160).HasColumnName("days_31_60");
            e.Property(a => a.Days6190).HasColumnName("days_61_90");
        });
    }

    /// <summary>A column the database computes: EF Core reads it and never writes it.</summary>
    private static void Generated(PropertyBuilder property)
    {
        property.ValueGeneratedOnAddOrUpdate();
        property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }

    internal static string SnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
