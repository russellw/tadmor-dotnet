using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>What distinguishes sales and purchase orders (spec/api.md §5.10).</summary>
public sealed record OrderKind
{
    public required string Collection { get; init; }
    public required string Label { get; init; }
    public required string Table { get; init; }
    public required string PartyField { get; init; }
    public required string ExpectedField { get; init; }
    public required string PriceField { get; init; }
    public required string AccountField { get; init; }
    /// <summary>invoiced_status or billed_status, and its line quantities.</summary>
    public required string Charged { get; init; }
    /// <summary>shipped_status or received_status, and its line quantities.</summary>
    public required string Moved { get; init; }
    public required string ChargedQty { get; init; }
    public required string MovedQty { get; init; }
    public required string ToCharge { get; init; }
    public required string ToMove { get; init; }
    /// <summary>The fulfilment body's number and date fields, and the response key.</summary>
    public required string ChargeNumber { get; init; }
    public required string ChargeDate { get; init; }
    public required string ChargeKey { get; init; }
    public required string MovementType { get; init; }
    public required string SourceType { get; init; }
    public required bool Sales { get; init; }

    public static readonly OrderKind SalesOrder = new()
    {
        Collection = "sales-orders", Label = "Sales Order", Table = "sales_orders", PartyField = "customer_id",
        ExpectedField = "expected_ship_date", PriceField = "unit_price", AccountField = "revenue_account_id",
        Charged = "invoiced_status", Moved = "shipped_status", ChargedQty = "qty_invoiced", MovedQty = "qty_shipped",
        ToCharge = "qty_to_invoice", ToMove = "qty_to_ship", ChargeNumber = "invoice_number", ChargeDate = "invoice_date",
        ChargeKey = "invoice_id", MovementType = "issue", SourceType = "sales_order_line", Sales = true,
    };

    public static readonly OrderKind PurchaseOrder = new()
    {
        Collection = "purchase-orders", Label = "Purchase Order", Table = "purchase_orders", PartyField = "supplier_id",
        ExpectedField = "expected_receipt_date", PriceField = "unit_cost", AccountField = "expense_account_id",
        Charged = "billed_status", Moved = "received_status", ChargedQty = "qty_billed", MovedQty = "qty_received",
        ToCharge = "qty_to_bill", ToMove = "qty_to_receive", ChargeNumber = "bill_number", ChargeDate = "bill_date",
        ChargeKey = "bill_id", MovementType = "receipt", SourceType = "purchase_order_line", Sales = false,
    };
}

/// <summary>
/// Sales and purchase orders (spec/domain.md §6): commercial documents
/// that never post. Drafts are edited and deleted freely; confirmed (open)
/// orders are fulfilled by draft invoices or bills and draft stock
/// movements that draw on their lines, and are closed by hand. Fulfilment
/// quantities and statuses are derived by the schema's views.
/// </summary>
public sealed class Orders<TOrder, TLine, TLineFul, TFul, TDoc, TDocLine>(TadmorDb db, OrderKind kind) : IOrders
    where TOrder : Order, new()
    where TLine : DocumentLine, new()
    where TLineFul : OrderLineFulfilment
    where TFul : OrderFulfilment
    where TDoc : LineDocument, new()
    where TDocLine : DocumentLine, new()
{
    public OrderKind Kind => kind;

    private sealed class OrderAndStatus
    {
        public required TOrder Order { get; init; }
        public TFul? Fulfilment { get; init; }
    }

    private IQueryable<OrderAndStatus> Rows =>
        from o in db.Set<TOrder>().AsNoTracking()
        join f in db.Set<TFul>() on o.Id equals f.OrderId into fs
        from f in fs.DefaultIfEmpty()
        select new OrderAndStatus { Order = o, Fulfilment = f };

    public async Task<List<Dictionary<string, object?>>> ListAsync() =>
        (await Rows.OrderByDescending(r => r.Order.OrderDate).ThenByDescending(r => r.Order.Id).ToListAsync())
        .Select(r => Shape(r.Order, r.Fulfilment)).ToList();

    public async Task<Dictionary<string, object?>> GetAsync(int id)
    {
        var r = await Rows.Where(r => r.Order.Id == id).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();
        return Shape(r.Order, r.Fulfilment);
    }

    private Dictionary<string, object?> Shape(TOrder o, TFul? f) => new()
    {
        ["id"] = o.Id,
        ["order_number"] = o.OrderNumber,
        [kind.PartyField] = o.PartyId,
        ["order_date"] = o.OrderDate,
        [kind.ExpectedField] = o.ExpectedDate,
        ["currency_code"] = o.CurrencyCode,
        ["status"] = o.Status,
        ["subtotal"] = o.Subtotal,
        ["tax_total"] = o.TaxTotal,
        ["total"] = o.Total,
        [kind.Charged] = f?.ChargedStatus ?? "none",
        [kind.Moved] = f?.MovedStatus ?? "none",
        ["reference"] = o.Reference,
        ["memo"] = o.Memo,
    };

    public async Task<List<Dictionary<string, object?>>> LinesAsync(int id)
    {
        if (!await db.Set<TOrder>().AnyAsync(o => o.Id == id))
        {
            throw ServiceException.NotFound();
        }
        var lines = await (from l in db.Set<TLine>().AsNoTracking()
                           join f in db.Set<TLineFul>() on l.Id equals f.OrderLineId
                           where l.DocumentId == id
                           orderby l.LineNo
                           select new { Line = l, Fulfilment = f }).ToListAsync();
        return lines.Select(x => new Dictionary<string, object?>
        {
            ["line_no"] = x.Line.LineNo,
            ["order_line_id"] = x.Line.Id,
            ["product_id"] = x.Line.ProductId,
            ["description"] = x.Line.Description,
            ["quantity"] = x.Line.Quantity,
            [kind.PriceField] = x.Line.Price,
            ["tax_code"] = x.Line.TaxCode,
            ["tax_rate"] = x.Line.TaxRate,
            ["line_subtotal"] = x.Line.LineSubtotal,
            ["tax_amount"] = x.Line.TaxAmount,
            ["line_total"] = x.Line.LineTotal,
            [kind.AccountField] = x.Line.AccountId,
            [kind.ChargedQty] = x.Fulfilment.QtyCharged,
            [kind.MovedQty] = x.Fulfilment.QtyMoved,
            [kind.ToCharge] = x.Fulfilment.QtyToCharge,
            [kind.ToMove] = x.Fulfilment.QtyToMove,
        }).ToList();
    }

    private sealed record Body(TOrder Header, List<TLine> Lines);

    private Body Parse(Input input)
    {
        var o = new TOrder
        {
            OrderNumber = input.ReqStr("order_number"),
            PartyId = input.ReqId(kind.PartyField),
            OrderDate = input.ReqDate("order_date"),
            ExpectedDate = input.Date(kind.ExpectedField),
            CurrencyCode = input.ReqStr("currency_code"),
            Reference = input.Str("reference"),
            Memo = input.Str("memo"),
        };
        var lines = input.List("lines").Select((l, i) =>
        {
            var line = new TLine
            {
                LineNo = i + 1,
                Description = l.ReqStr("description"),
                ProductId = l.Int("product_id"),
                Quantity = l.Dec("quantity", Scale.Money) ?? 1,
                Price = l.Dec(kind.PriceField, Scale.Money) ?? 0,
                AccountId = l.Int(kind.AccountField),
                TaxCode = l.Str("tax_code"),
                TaxRate = l.Dec("tax_rate", Scale.Rate) ?? 0,
            };
            if (line.Quantity <= 0)
            {
                throw ServiceException.Unprocessable($"line {i + 1}: an order line's quantity must be positive");
            }
            return line;
        }).ToList();
        return new Body(o, lines);
    }

    private static void Copy(TOrder to, TOrder from)
    {
        to.OrderNumber = from.OrderNumber;
        to.PartyId = from.PartyId;
        to.OrderDate = from.OrderDate;
        to.ExpectedDate = from.ExpectedDate;
        to.CurrencyCode = from.CurrencyCode;
        to.Reference = from.Reference;
        to.Memo = from.Memo;
    }

    public async Task<int> CreateAsync(Input input)
    {
        var body = Parse(input);
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Set<TOrder>().Add(body.Header);
        await db.SaveChangesAsync();
        await InsertLinesAsync(body.Header.Id, body.Lines);
        await tx.CommitAsync();
        return body.Header.Id;
    }

    public async Task UpdateAsync(int id, Input input)
    {
        var body = Parse(input);
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await LockAsync(id);
        RequireStatus(o, "draft", "edited");
        Copy(o, body.Header);
        await db.SaveChangesAsync();
        await db.Set<TLine>().Where(l => l.DocumentId == id).ExecuteDeleteAsync();
        await InsertLinesAsync(id, body.Lines);
        await tx.CommitAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await LockAsync(id);
        RequireStatus(o, "draft", "deleted");
        db.Set<TOrder>().Remove(o);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task InsertLinesAsync(int orderId, List<TLine> lines)
    {
        foreach (var line in lines)
        {
            line.DocumentId = orderId;
            db.Set<TLine>().Add(line);
        }
        await db.SaveChangesAsync();
    }

    private async Task<TOrder> LockAsync(int id)
    {
        if (await db.ScalarAsync<int?>($"SELECT id FROM {kind.Table} WHERE id = {{0}} FOR UPDATE", id) is null)
        {
            throw ServiceException.NotFound();
        }
        var o = await db.Set<TOrder>().SingleAsync(o => o.Id == id);
        await db.Entry(o).ReloadAsync();
        return o;
    }

    private void RequireStatus(TOrder o, string status, string action)
    {
        if (o.Status != status)
        {
            throw ServiceException.Conflict($"{kind.Label} {o.OrderNumber} is {o.Status}; only a {status} order can be {action}");
        }
    }

    /// <summary>Draft to open; the order needs a line (422).</summary>
    public async Task ConfirmAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await LockAsync(id);
        RequireStatus(o, "draft", "confirmed");
        if (!await db.Set<TLine>().AnyAsync(l => l.DocumentId == id))
        {
            throw ServiceException.Unprocessable($"{kind.Label} {o.OrderNumber} has no lines");
        }
        o.Status = "open";
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Open to closed, by hand.</summary>
    public async Task CloseAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await LockAsync(id);
        RequireStatus(o, "open", "closed");
        o.Status = "closed";
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>A draft always cancels; an open order only while nothing is fulfilled against it.</summary>
    public async Task CancelAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await LockAsync(id);
        if (o.Status is not ("draft" or "open"))
        {
            throw ServiceException.Conflict($"{kind.Label} {o.OrderNumber} is {o.Status} and cannot be cancelled");
        }
        if (o.Status == "open" && await db.Set<TLineFul>().AnyAsync(f => f.OrderId == id && (f.QtyCharged != 0 || f.QtyMoved != 0)))
        {
            throw ServiceException.Conflict($"{kind.Label} {o.OrderNumber} has been fulfilled in part and cannot be cancelled");
        }
        o.Status = "cancelled";
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>The requested quantity per order line; null when the request names none, meaning all that remains.</summary>
    private static Dictionary<int, decimal?>? Requested(Input input)
    {
        var lines = input.List("lines");
        if (lines.Count == 0)
        {
            return null;
        }
        var requested = new Dictionary<int, decimal?>();
        foreach (var l in lines)
        {
            requested[l.ReqId("order_line_id")] = l.Dec("quantity", Scale.Money);
        }
        return requested;
    }

    private static decimal Take(decimal remaining, Dictionary<int, decimal?>? requested, int lineId)
    {
        if (requested is null)
        {
            return remaining;
        }
        if (!requested.TryGetValue(lineId, out var want))
        {
            return 0;
        }
        return Math.Max(0, Math.Min(remaining, want ?? remaining));
    }

    /// <summary>
    /// Invoices a sales order (bills a purchase order): a draft document in
    /// the order's party and currency, with the order number as reference,
    /// whose lines copy the order lines and take min(remaining, requested),
    /// numbered in order-line order, lines that come to zero skipped. The
    /// order must be open (409); nothing left to charge is a 422.
    /// </summary>
    public async Task<int> ChargeAsync(int id, Input input)
    {
        var number = input.ReqStr(kind.ChargeNumber);
        var date = input.ReqDate(kind.ChargeDate);
        var due = input.Date("due_date");
        var requested = Requested(input);
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await LockAsync(id);
        RequireStatus(o, "open", kind.Sales ? "invoiced" : "billed");

        var lines = await (from l in db.Set<TLine>().AsNoTracking()
                           join f in db.Set<TLineFul>() on l.Id equals f.OrderLineId
                           where l.DocumentId == id
                           orderby l.Id
                           select new { Line = l, f.QtyToCharge }).ToListAsync();
        var docLines = new List<TDocLine>();
        foreach (var x in lines)
        {
            var qty = Take(x.QtyToCharge, requested, x.Line.Id);
            if (qty <= 0)
            {
                continue;
            }
            docLines.Add(new TDocLine
            {
                LineNo = docLines.Count + 1, ProductId = x.Line.ProductId, Description = x.Line.Description, Quantity = qty,
                Price = x.Line.Price, AccountId = x.Line.AccountId, TaxCode = x.Line.TaxCode, TaxRate = x.Line.TaxRate,
                OrderLineId = x.Line.Id,
            });
        }
        if (docLines.Count == 0)
        {
            throw ServiceException.Unprocessable($"{kind.Label} {o.OrderNumber} has nothing left to {(kind.Sales ? "invoice" : "bill")}");
        }
        var doc = new TDoc
        {
            Number = number, PartyId = o.PartyId, Date = date, DueDate = due, CurrencyCode = o.CurrencyCode, Reference = o.OrderNumber,
        };
        db.Set<TDoc>().Add(doc);
        await db.SaveChangesAsync();
        foreach (var l in docLines)
        {
            l.DocumentId = doc.Id;
            db.Set<TDocLine>().Add(l);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return doc.Id;
    }

    /// <summary>
    /// Ships a sales order (receives a purchase order): one draft movement
    /// per eligible line (a tracked, active product with quantity left to
    /// move), from or into the given warehouse. An issue costs the
    /// warehouse's moving-average cost for the product; a receipt is valued
    /// in the base currency at the order's cost times the rate for the
    /// movement date (422, and nothing created, if there is none). The order
    /// must be open (409); nothing to move is a 422.
    /// </summary>
    public async Task<List<int>> MoveAsync(int id, Input input)
    {
        var warehouse = input.ReqId("warehouse_id");
        var date = input.Date("movement_date") ?? Stock.Today;
        var reference = input.Str("reference");
        var requested = Requested(input);
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await LockAsync(id);
        RequireStatus(o, "open", kind.Sales ? "shipped" : "received");

        var lines = await (from l in db.Set<TLine>().AsNoTracking()
                           join f in db.Set<TLineFul>() on l.Id equals f.OrderLineId
                           join p in db.Products on l.ProductId equals p.Id
                           where l.DocumentId == id && p.TrackInventory && p.IsActive
                           orderby l.Id
                           select new { Line = l, f.QtyToMove }).ToListAsync();
        var created = new List<int>();
        foreach (var x in lines)
        {
            var qty = Take(x.QtyToMove, requested, x.Line.Id);
            if (qty <= 0)
            {
                continue;
            }
            decimal cost;
            if (kind.Sales)
            {
                cost = await db.ScalarAsync<decimal?>(
                    "SELECT avg_unit_cost FROM stock_on_hand WHERE product_id = {0} AND warehouse_id = {1}",
                    x.Line.ProductId!.Value, warehouse) ?? 0;
            }
            else
            {
                cost = await db.ScalarAsync<decimal?>("""
                    SELECT round({0}::numeric * CASE WHEN {1}::text = (SELECT base_currency FROM gl_settings) THEN 1::numeric
                        ELSE (SELECT rate FROM exchange_rates WHERE currency_code = {1}::text AND rate_date <= {2}
                              ORDER BY rate_date DESC LIMIT 1) END, 4)
                    """, x.Line.Price, o.CurrencyCode, date)
                    ?? throw ServiceException.Unprocessable($"no exchange rate for {o.CurrencyCode} on or before {date:yyyy-MM-dd}");
            }
            var m = new StockMovement
            {
                ProductId = x.Line.ProductId!.Value, WarehouseId = warehouse, MovementDate = date, MovementType = kind.MovementType,
                Quantity = kind.Sales ? -qty : qty, UnitCost = cost, SourceType = kind.SourceType, SourceId = x.Line.Id,
                Reference = reference,
            };
            db.StockMovements.Add(m);
            await db.SaveChangesAsync();
            created.Add(m.Id);
        }
        if (created.Count == 0)
        {
            throw ServiceException.Unprocessable($"{kind.Label} {o.OrderNumber} has nothing left to {(kind.Sales ? "ship" : "receive")}");
        }
        await tx.CommitAsync();
        return created;
    }
}
