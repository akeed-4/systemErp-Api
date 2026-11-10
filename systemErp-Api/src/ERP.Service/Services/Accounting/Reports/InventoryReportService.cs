using ERP.Service.Services.Shared;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تقارير المخزون والحركة التجارية. التقارير القائمية مبنية كاستعلامات IQueryable مُسقَطة فتُنفَّذ
/// الفلترة والفرز والترقيم والملخصات في قاعدة البيانات؛ كارت الصنف وحده يُحسب في الذاكرة (رصيد جارٍ) لصنف واحد وفترة واحدة.
/// </summary>
public class InventoryReportService : IInventoryReportService
{
    private readonly ErpDbContext _db;
    public InventoryReportService(ErpDbContext db) => _db = db;

    private static int Sign(StockMovementType t) => t is StockMovementType.InPurchase or StockMovementType.AdjustmentIn ? 1 : -1;

    private IQueryable<Product> ProductsQuery(ReportQueryDto q)
    {
        var p = _db.Set<Product>().AsNoTracking().AsQueryable();
        if (q.ItemId.HasValue) p = p.Where(x => x.Id == q.ItemId);
        if (!string.IsNullOrWhiteSpace(q.Category)) p = p.Where(x => x.Category == q.Category);
        return p;
    }

    // ---------- الاستعلامات (تُنفَّذ في قاعدة البيانات) ----------

    private IQueryable<InventoryAuditRowDto> InventoryAuditQuery(ReportQueryDto q)
        => ProductsQuery(q).OrderBy(p => p.Sku).Select(p => new InventoryAuditRowDto
        {
            ItemId = p.Id, Sku = p.Sku, Barcode = p.Barcode, ItemName = p.NameAr, Category = p.Category, Unit = p.Unit,
            SystemQuantity = p.CurrentStock, AverageUnitCost = p.AverageCost, SystemValuation = Math.Round(p.CurrentStock * p.AverageCost, 2),
        });

    /// <summary>ملخص حركة كل صنف: الافتتاحي والوارد والمنصرف والختامي استعلامات فرعية مجمّعة في SQL لكل صنف.</summary>
    private IQueryable<ItemMovementSummaryRowDto> ItemMovementsQuery(ReportQueryDto q)
    {
        var movements = _db.Set<StockMovement>().AsNoTracking();
        var before = q.DateFrom.HasValue ? movements.Where(m => m.Date < q.DateFrom) : movements.Where(m => false);
        var inPeriod = movements;
        if (q.DateFrom.HasValue) inPeriod = inPeriod.Where(m => m.Date >= q.DateFrom);
        if (q.DateTo.HasValue) inPeriod = inPeriod.Where(m => m.Date <= q.DateTo);

        return ProductsQuery(q).OrderBy(p => p.Sku)
            .Select(p => new
            {
                p,
                Opening = before.Where(m => m.ItemId == p.Id)
                    .Sum(m => (decimal?)(m.Type == StockMovementType.InPurchase || m.Type == StockMovementType.AdjustmentIn ? m.Quantity : -m.Quantity)) ?? 0,
                Net = inPeriod.Where(m => m.ItemId == p.Id)
                    .Sum(m => (decimal?)(m.Type == StockMovementType.InPurchase || m.Type == StockMovementType.AdjustmentIn ? m.Quantity : -m.Quantity)) ?? 0,
                PurchaseIn = inPeriod.Where(m => m.ItemId == p.Id && m.Type == StockMovementType.InPurchase).Sum(m => (decimal?)m.Quantity) ?? 0,
                SalesOut = inPeriod.Where(m => m.ItemId == p.Id && m.Type == StockMovementType.OutSales).Sum(m => (decimal?)m.Quantity) ?? 0,
                Returns = inPeriod.Where(m => m.ItemId == p.Id && (m.ReferenceNumber.StartsWith("SRET-") || m.ReferenceNumber.StartsWith("PRET-")))
                    .Sum(m => (decimal?)m.Quantity) ?? 0,
            })
            .Select(x => new ItemMovementSummaryRowDto
            {
                ItemId = x.p.Id, Sku = x.p.Sku, ItemName = x.p.NameAr, Category = x.p.Category, Unit = x.p.Unit,
                OpeningStock = x.Opening, PurchaseInQty = x.PurchaseIn, SalesOutQty = x.SalesOut, ReturnsQty = x.Returns,
                ClosingStock = x.Opening + x.Net,
                AverageCost = x.p.AverageCost, TotalStockValue = Math.Round((x.Opening + x.Net) * x.p.AverageCost, 2),
            });
    }

    /// <summary>
    /// كارت الصنف يحتاج رصيدًا جاريًا متسلسلًا، فيُحسب في الذاكرة لصنف واحد فقط:
    /// الرصيد الافتتاحي قبل الفترة يُجمَّع في SQL، ثم تُجلب حركات الفترة وحدها (لا كل تاريخ الصنف).
    /// </summary>
    private async Task<List<DetailedItemLedgerEntryDto>> ItemLedgerRowsAsync(ReportQueryDto q, CancellationToken ct)
    {
        if (!q.ItemId.HasValue) throw new ValidationFailedException("حدّد الصنف لعرض كارت الصنف.");
        var itemMovements = _db.Set<StockMovement>().AsNoTracking().Where(m => m.ItemId == q.ItemId);

        decimal qty = 0, value = 0;
        if (q.DateFrom.HasValue)
        {
            var opening = itemMovements.Where(m => m.Date < q.DateFrom)
                .Select(m => new
                {
                    Qty = m.Type == StockMovementType.InPurchase || m.Type == StockMovementType.AdjustmentIn ? m.Quantity : -m.Quantity,
                    m.UnitCost,
                });
            qty = await opening.SumAsync(x => (decimal?)x.Qty, ct) ?? 0;
            value = await opening.SumAsync(x => (decimal?)(x.Qty * x.UnitCost), ct) ?? 0;
        }

        var period = itemMovements;
        if (q.DateFrom.HasValue) period = period.Where(m => m.Date >= q.DateFrom);
        if (q.DateTo.HasValue) period = period.Where(m => m.Date <= q.DateTo);
        var list = await period.OrderBy(m => m.Date).ThenBy(m => m.CreatedAt).ToListAsync(ct);

        var rows = new List<DetailedItemLedgerEntryDto>(list.Count);
        foreach (var m in list)
        {
            var sign = Sign(m.Type);
            qty += sign * m.Quantity;
            value += sign * m.Quantity * m.UnitCost;
            rows.Add(new DetailedItemLedgerEntryDto
            {
                Id = m.Id, ItemId = m.ItemId, Date = m.Date, DocType = m.Type.ToString(), DocNumber = m.ReferenceNumber,
                QuantityIn = sign > 0 ? m.Quantity : 0, QuantityOut = sign < 0 ? m.Quantity : 0,
                UnitCost = m.UnitCost, UnitPrice = m.UnitPrice, TotalValue = Math.Round(m.Quantity * m.UnitCost, 2),
                RunningStockBalance = qty, RunningStockValue = Math.Round(value, 2),
            });
        }
        return rows;
    }

    private IQueryable<CommercialTradeRowDto> CommercialTradeQuery(ReportQueryDto q)
    {
        var inv = _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == "posted");
        if (q.DateFrom.HasValue) inv = inv.Where(i => i.IssueDate >= q.DateFrom);
        if (q.DateTo.HasValue) inv = inv.Where(i => i.IssueDate <= q.DateTo);
        return inv.OrderBy(i => i.IssueDate).Select(i => new CommercialTradeRowDto
        {
            DocNumber = i.InvoiceNumber, Date = i.IssueDate, Type = i.Kind, PartyName = i.PartyName, VatNumber = i.PartyVatNumber,
            ItemsCount = i.Items.Count(), Subtotal = i.Subtotal, DiscountTotal = i.DiscountTotal, VatTotal = i.VatTotal, GrandTotal = i.GrandTotal,
            CogsTotal = i.TotalCost, GrossProfit = i.GrossProfit,
            GrossProfitMarginPercent = i.Subtotal == 0 ? 0 : Math.Round(i.GrossProfit / i.Subtotal * 100, 2),
            ZatcaStatus = i.ZatcaStatus,
        });
    }

    // ---------- القوائم الكاملة ----------

    public Task<List<InventoryAuditRowDto>> GetInventoryAuditAsync(ReportQueryDto q, CancellationToken ct = default)
        => InventoryAuditQuery(q).ToListAsync(ct);
    public Task<List<ItemMovementSummaryRowDto>> GetItemMovementsAsync(ReportQueryDto q, CancellationToken ct = default)
        => ItemMovementsQuery(q).ToListAsync(ct);
    public Task<List<DetailedItemLedgerEntryDto>> GetItemLedgerAsync(ReportQueryDto q, CancellationToken ct = default)
        => ItemLedgerRowsAsync(q, ct);
    public Task<List<CommercialTradeRowDto>> GetCommercialTradeAsync(ReportQueryDto q, CancellationToken ct = default)
        => CommercialTradeQuery(q).ToListAsync(ct);

    // ---------- تقارير DevExtreme ----------

    public Task<LoadResult> LoadInventoryAuditAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(InventoryAuditQuery(q), options, ct, "Sku", "ItemId");
    public Task<LoadResult> LoadItemMovementsAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(ItemMovementsQuery(q), options, ct, "Sku", "ItemId");
    public async Task<LoadResult> LoadItemLedgerAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await ItemLedgerRowsAsync(q, ct), options, ct);
    public Task<LoadResult> LoadCommercialTradeAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(CommercialTradeQuery(q), options, ct, "Date", "DocNumber");
}
