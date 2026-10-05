using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>
/// Stock movements, the append-only quantity ledger (spec/api.md §5.12,
/// spec/domain.md §2 and §4). Quantity on hand and value are sums over the
/// movements, posted or not. Only receipts and issues post to the ledger,
/// in the base currency. The schema refuses a quantity whose sign disagrees
/// with the type, an unknown type, a negative cost, and a product that is
/// not tracked and active (422).
/// </summary>
public sealed class Stock(TadmorDb db, Journal journal)
{
    public sealed record MovementDto(int Id, int ProductId, int WarehouseId, DateOnly MovementDate, string MovementType,
        string Status, decimal Quantity, decimal UnitCost, decimal TotalCost, string? Reference, string? Notes,
        int? JournalEntryId, string? SourceType, int? SourceId);

    private static MovementDto Shape(StockMovement m) => new(m.Id, m.ProductId, m.WarehouseId, m.MovementDate, m.MovementType,
        m.JournalEntryId is null ? "draft" : "posted", m.Quantity, m.UnitCost, m.TotalCost, m.Reference, m.Notes,
        m.JournalEntryId, m.SourceType, m.SourceId);

    public async Task<List<MovementDto>> ListAsync() =>
        (await db.StockMovements.AsNoTracking().OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Id).ToListAsync())
        .Select(Shape).ToList();

    public async Task<MovementDto> GetAsync(int id) =>
        Shape(await db.StockMovements.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id) ?? throw ServiceException.NotFound());

    /// <summary>Today, the UTC date (spec/api.md §1.2).</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Fill(StockMovement m, Input input)
    {
        m.ProductId = input.ReqId("product_id");
        m.WarehouseId = input.ReqId("warehouse_id");
        m.MovementType = input.ReqStr("movement_type");
        m.Quantity = input.ReqDec("quantity", Scale.Money);
        m.MovementDate = input.Date("movement_date") ?? Today;
        m.UnitCost = input.Dec("unit_cost", Scale.Money) ?? 0;
        m.Reference = input.Str("reference");
        m.Notes = input.Str("notes");
        if (m.Quantity == 0)
        {
            throw ServiceException.Unprocessable("quantity must not be zero");
        }
    }

    public async Task<int> CreateAsync(Input input)
    {
        var m = new StockMovement { MovementType = "" };
        Fill(m, input);
        db.StockMovements.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    /// <summary>Unposted, hand-entered movements only: a fulfilment movement cannot be edited (409).</summary>
    public async Task UpdateAsync(int id, Input input)
    {
        Fill(new StockMovement { MovementType = "" }, input);
        await using var tx = await db.Database.BeginTransactionAsync();
        var m = await LockAsync(id);
        if (m.JournalEntryId is not null)
        {
            throw ServiceException.Conflict($"stock movement {id} is posted");
        }
        if (m.SourceType is not null)
        {
            throw ServiceException.Conflict($"stock movement {id} was produced by order fulfilment and cannot be edited");
        }
        Fill(m, input);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Unposted movements only; fulfilment movements may be deleted, returning their quantity to the order.</summary>
    public async Task DeleteAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var m = await LockAsync(id);
        if (m.JournalEntryId is not null)
        {
            throw ServiceException.Conflict($"stock movement {id} is posted");
        }
        db.StockMovements.Remove(m);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task<StockMovement> LockAsync(int id)
    {
        if (await db.ScalarAsync<int?>("SELECT id FROM stock_movements WHERE id = {0} FOR UPDATE", id) is null)
        {
            throw ServiceException.NotFound();
        }
        var m = await db.StockMovements.SingleAsync(m => m.Id == id);
        await db.Entry(m).ReloadAsync();
        return m;
    }

    /// <summary>
    /// Posts a receipt or an issue, in the base currency, dated on the
    /// movement. An issue debits the product's COGS and credits its
    /// inventory; a receipt debits inventory and credits the account the
    /// body names (credit_account_id, postable and active). Refused (409) if
    /// already posted, and (422) for zero cost, another type, missing
    /// product accounts, or no open period.
    /// </summary>
    public async Task<int> PostAsync(int id, Input input)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var m = await LockAsync(id);
        if (m.JournalEntryId is not null)
        {
            throw ServiceException.Conflict($"stock movement {id} is already posted");
        }
        if (m.TotalCost == 0)
        {
            throw ServiceException.Unprocessable($"stock movement {id} has nothing to post: its total cost is zero");
        }
        if (m.MovementType is not ("receipt" or "issue"))
        {
            throw ServiceException.Unprocessable($"only receipts and issues post; this is a {m.MovementType}");
        }
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == m.ProductId);
        var inventory = product.InventoryAccountId
            ?? throw ServiceException.Unprocessable($"product {product.Sku} has no inventory account");
        int other;
        if (m.MovementType == "issue")
        {
            other = product.CogsAccountId ?? throw ServiceException.Unprocessable($"product {product.Sku} has no COGS account");
        }
        else
        {
            var credit = input.Int("credit_account_id")
                ?? throw ServiceException.Unprocessable("a receipt needs a credit_account_id to credit");
            if (!await db.Accounts.AnyAsync(a => a.Id == credit && a.IsPostable && a.IsActive))
            {
                throw ServiceException.Unprocessable($"account {credit} is not a postable, active account");
            }
            other = credit;
        }

        var period = await journal.PeriodForAsync(m.MovementDate);
        var entry = await journal.CreateEntryAsync(m.MovementDate, period, await journal.BaseCurrencyAsync(),
            $"Stock {m.MovementType} {m.Id}", null);
        var cost = Math.Abs(m.TotalCost);
        if (m.MovementType == "issue")
        {
            await journal.AddLineAsync(entry, other, cost, "Cost of goods sold");
            await journal.AddLineAsync(entry, inventory, -cost, "Inventory");
        }
        else
        {
            await journal.AddLineAsync(entry, inventory, cost, "Inventory");
            await journal.AddLineAsync(entry, other, -cost, "Receipt");
        }
        m.JournalEntryId = entry;
        m.PeriodId = period;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return entry;
    }

    /// <summary>Unposts (administrators only): reverses the entry and unlinks it; the quantity record stays.</summary>
    public async Task<int> UnpostAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var m = await LockAsync(id);
        if (m.JournalEntryId is not { } entry)
        {
            throw ServiceException.Conflict($"stock movement {id} is not posted");
        }
        var reversal = await journal.ReverseAsync(entry);
        m.JournalEntryId = null;
        m.PeriodId = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return reversal;
    }

    public sealed class ValuationRow
    {
        public int ProductId { get; set; }
        public string Sku { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal QtyOnHand { get; set; }
        public decimal ValueOnHand { get; set; }
        public decimal AvgUnitCost { get; set; }
    }

    /// <summary>Per product with movements, across warehouses and posted or not, by sku.</summary>
    public Task<List<ValuationRow>> ValuationAsync() => db.ListAsync<ValuationRow>("""
        SELECT v.product_id AS "ProductId", p.sku AS "Sku", p.name AS "Name", v.qty_on_hand AS "QtyOnHand",
            v.value_on_hand AS "ValueOnHand", v.avg_unit_cost AS "AvgUnitCost"
        FROM stock_valuation v JOIN products p ON p.id = v.product_id
        ORDER BY p.sku, p.id
        """);
}
