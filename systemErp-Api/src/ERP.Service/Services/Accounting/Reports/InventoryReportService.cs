using ERP.Service.Services.Shared;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Accounting;

public class InventoryReportService : IInventoryReportService
{
    private readonly ErpDbContext _db;
    public InventoryReportService(ErpDbContext db) => _db = db;

    // على مستوى المنشأة التحويل بين المستودعات أثره صفر؛ وعلى مستوى مستودع بعينه هو وارد أو صادر
    private static int Sign(StockMovementType t, bool warehouseLevel = false) => t switch
    {
        StockMovementType.InPurchase or StockMovementType.AdjustmentIn => 1,
        StockMovementType.TransferIn => warehouseLevel ? 1 : 0,
        StockMovementType.TransferOut => warehouseLevel ? -1 : 0,
        _ => -1,
    };

    private IQueryable<StockMovement> Movements(ReportQueryDto q)
    {
        var m = _db.Set<StockMovement>().AsNoTracking().AsQueryable();
        return q.WarehouseId.HasValue ? m.Where(x => x.WarehouseId == q.WarehouseId) : m;
    }
    private static bool IsReturnRef(string r) => r.StartsWith("SRET-") || r.StartsWith("PRET-");

    private IQueryable<Product> ProductsQuery(ReportQueryDto q)
    {
        var p = _db.Set<Product>().AsNoTracking().AsQueryable();
        if (q.ItemId.HasValue) p = p.Where(x => x.Id == q.ItemId);
        if (!string.IsNullOrWhiteSpace(q.Category)) p = p.Where(x => x.Category == q.Category);
        return p;
    }

    public async Task<List<InventoryAuditRowDto>> GetInventoryAuditAsync(ReportQueryDto q, CancellationToken ct = default)
    {
        // لمستودع بعينه: الكمية رصيده فيه، بالتكلفة الموحّدة للصنف
        var inWarehouse = q.WarehouseId.HasValue
            ? await _db.Set<WarehouseStock>().AsNoTracking().Where(s => s.WarehouseId == q.WarehouseId).ToDictionaryAsync(s => s.ItemId, s => s.Quantity, ct)
            : null;
        return (await ProductsQuery(q).OrderBy(p => p.Sku).ToListAsync(ct)).Select(p =>
        {
            var quantity = inWarehouse == null ? p.CurrentStock : inWarehouse.GetValueOrDefault(p.Id);
            return new InventoryAuditRowDto
            {
                ItemId = p.Id, Sku = p.Sku, Barcode = p.Barcode, ItemName = p.NameAr, Category = p.Category, Unit = p.Unit,
                SystemQuantity = quantity, AverageUnitCost = p.AverageCost, SystemValuation = Math.Round(quantity * p.AverageCost, 2),
            };
        }).ToList();
    }

    public async Task<List<ItemMovementSummaryRowDto>> GetItemMovementsAsync(ReportQueryDto q, CancellationToken ct = default)
    {
        var products = await ProductsQuery(q).OrderBy(p => p.Sku).ToListAsync(ct);
        var ids = products.Select(p => p.Id).ToList();
        var movements = (await Movements(q).Where(m => ids.Contains(m.ItemId)).ToListAsync(ct))
            .GroupBy(m => m.ItemId).ToDictionary(g => g.Key, g => g.ToList());
        var byWarehouse = q.WarehouseId.HasValue;
        int Signed(StockMovementType t) => Sign(t, byWarehouse);

        return products.Select(p =>
        {
            var list = movements.GetValueOrDefault(p.Id) ?? new();
            var before = list.Where(m => q.DateFrom.HasValue && m.Date < q.DateFrom).Sum(m => Signed(m.Type) * m.Quantity);
            var inPeriod = list.Where(m => (!q.DateFrom.HasValue || m.Date >= q.DateFrom) && (!q.DateTo.HasValue || m.Date <= q.DateTo)).ToList();
            return new ItemMovementSummaryRowDto
            {
                ItemId = p.Id, Sku = p.Sku, ItemName = p.NameAr, Category = p.Category, Unit = p.Unit,
                OpeningStock = before,
                PurchaseInQty = inPeriod.Where(m => m.Type == StockMovementType.InPurchase).Sum(m => m.Quantity),
                SalesOutQty = inPeriod.Where(m => m.Type == StockMovementType.OutSales).Sum(m => m.Quantity),
                ReturnsQty = inPeriod.Where(m => IsReturnRef(m.ReferenceNumber)).Sum(m => m.Quantity),
                ClosingStock = before + inPeriod.Sum(m => Signed(m.Type) * m.Quantity),
                AverageCost = p.AverageCost, TotalStockValue = Math.Round((before + inPeriod.Sum(m => Signed(m.Type) * m.Quantity)) * p.AverageCost, 2),
            };
        }).ToList();
    }

    public async Task<List<DetailedItemLedgerEntryDto>> GetItemLedgerAsync(ReportQueryDto q, CancellationToken ct = default)
    {
        if (!q.ItemId.HasValue) throw new ValidationFailedException(Messages.SelectItemForItemCard);
        var all = await Movements(q).Where(m => m.ItemId == q.ItemId)
            .OrderBy(m => m.Date).ThenBy(m => m.CreatedAt).ToListAsync(ct);

        decimal qty = 0, value = 0;
        var rows = new List<DetailedItemLedgerEntryDto>();
        foreach (var m in all)
        {
            var sign = Sign(m.Type, q.WarehouseId.HasValue);
            if (sign == 0) continue;
            qty += sign * m.Quantity;
            value += sign * m.Quantity * m.UnitCost;
            if ((q.DateFrom.HasValue && m.Date < q.DateFrom) || (q.DateTo.HasValue && m.Date > q.DateTo)) continue;
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

    public async Task<List<CommercialTradeRowDto>> GetCommercialTradeAsync(ReportQueryDto q, CancellationToken ct = default)
    {
        var inv = _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == "posted");
        if (q.DateFrom.HasValue) inv = inv.Where(i => i.IssueDate >= q.DateFrom);
        if (q.DateTo.HasValue) inv = inv.Where(i => i.IssueDate <= q.DateTo);
        var list = await inv.Include(i => i.Items).OrderBy(i => i.IssueDate).ToListAsync(ct);
        return list.Select(i => new CommercialTradeRowDto
        {
            DocNumber = i.InvoiceNumber, Date = i.IssueDate, Type = i.Kind, PartyName = i.PartyName, VatNumber = i.PartyVatNumber,
            ItemsCount = i.Items.Count, Subtotal = i.Subtotal, DiscountTotal = i.DiscountTotal, VatTotal = i.VatTotal, GrandTotal = i.GrandTotal,
            CogsTotal = i.TotalCost, GrossProfit = i.GrossProfit,
            GrossProfitMarginPercent = i.Subtotal == 0 ? 0 : Math.Round(i.GrossProfit / i.Subtotal * 100, 2),
            ZatcaStatus = i.ZatcaStatus,
        }).ToList();
    }

    public async Task<LoadResult> LoadInventoryAuditAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetInventoryAuditAsync(q, ct), options, ct);
    public async Task<LoadResult> LoadItemMovementsAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetItemMovementsAsync(q, ct), options, ct);
    public async Task<LoadResult> LoadItemLedgerAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetItemLedgerAsync(q, ct), options, ct);
    public async Task<LoadResult> LoadCommercialTradeAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetCommercialTradeAsync(q, ct), options, ct);
}
