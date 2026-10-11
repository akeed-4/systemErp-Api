using ERP.Service.Services.Shared;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Accounting;

/// <summary>تقارير المخزون: كل تقرير استعلام يُنفَّذ في قاعدة البيانات (مع خيارات DevExtreme)، ولا تُحمَّل الحركات إلى الذاكرة.</summary>
public class InventoryReportService : IInventoryReportService
{
    private readonly ErpDbContext _db;
    public InventoryReportService(ErpDbContext db) => _db = db;

    private sealed class SignedMovement
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }
        public DateTime Date { get; set; }
        public DateTime CreatedAt { get; set; }
        public StockMovementType Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal? UnitPrice { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;
        /// <summary>+1 وارد، −1 صادر، 0 بلا أثر.</summary>
        public int Sign { get; set; }
    }

    private IQueryable<StockMovement> Movements(ReportQueryDto q)
    {
        var m = _db.Set<StockMovement>().AsNoTracking().AsQueryable();
        return q.WarehouseId.HasValue ? m.Where(x => x.WarehouseId == q.WarehouseId) : m;
    }

    // على مستوى المنشأة التحويل بين المستودعات أثره صفر؛ وعلى مستوى مستودع بعينه هو وارد أو صادر
    private IQueryable<SignedMovement> SignedMovements(ReportQueryDto q)
    {
        var transfer = q.WarehouseId.HasValue ? 1 : 0;
        return Movements(q).Select(m => new SignedMovement
        {
            Id = m.Id, ItemId = m.ItemId, Date = m.Date, CreatedAt = m.CreatedAt, Type = m.Type, Quantity = m.Quantity,
            UnitCost = m.UnitCost, UnitPrice = m.UnitPrice, ReferenceNumber = m.ReferenceNumber,
            Sign = m.Type == StockMovementType.InPurchase || m.Type == StockMovementType.AdjustmentIn ? 1
                : m.Type == StockMovementType.TransferIn ? transfer
                : m.Type == StockMovementType.TransferOut ? -transfer
                : -1,
        });
    }

    private IQueryable<Product> ProductsQuery(ReportQueryDto q)
    {
        var p = _db.Set<Product>().AsNoTracking().AsQueryable();
        if (q.ItemId.HasValue) p = p.Where(x => x.Id == q.ItemId);
        if (!string.IsNullOrWhiteSpace(q.Category)) p = p.Where(x => x.Category == q.Category);
        return p;
    }

    private IQueryable<InventoryAuditRowDto> InventoryAuditQuery(ReportQueryDto q)
    {
        // لمستودع بعينه: الكمية رصيده فيه، بالتكلفة الموحّدة للصنف
        var stocks = _db.Set<WarehouseStock>().AsNoTracking().Where(s => s.WarehouseId == q.WarehouseId);
        var rows = q.WarehouseId.HasValue
            ? ProductsQuery(q).Select(p => new { p, Quantity = stocks.Where(s => s.ItemId == p.Id).Sum(s => s.Quantity) })
            : ProductsQuery(q).Select(p => new { p, Quantity = p.CurrentStock });
        return rows.Select(r => new InventoryAuditRowDto
        {
            ItemId = r.p.Id, Sku = r.p.Sku, Barcode = r.p.Barcode, ItemName = r.p.NameAr, Category = r.p.Category, Unit = r.p.Unit,
            SystemQuantity = r.Quantity, AverageUnitCost = r.p.AverageCost, SystemValuation = Math.Round(r.Quantity * r.p.AverageCost, 2),
        });
    }

    private IQueryable<ItemMovementSummaryRowDto> ItemMovementsQuery(ReportQueryDto q)
    {
        DateTime? start = q.DateFrom, end = q.DateTo;
        var hasStart = start.HasValue;
        var movements = SignedMovements(q);
        if (end.HasValue) movements = movements.Where(m => m.Date <= end);

        // مجاميع الحركات لكل صنف في مرور واحد: ما قبل الفترة رصيد افتتاحي، وما فيها حركتها
        var totals = from m in movements
                     group m by m.ItemId into g
                     select new
                     {
                         ItemId = g.Key,
                         Opening = g.Sum(m => hasStart && m.Date < start ? m.Sign * m.Quantity : 0m),
                         Net = g.Sum(m => !hasStart || m.Date >= start ? m.Sign * m.Quantity : 0m),
                         PurchaseIn = g.Sum(m => (!hasStart || m.Date >= start) && m.Type == StockMovementType.InPurchase ? m.Quantity : 0m),
                         SalesOut = g.Sum(m => (!hasStart || m.Date >= start) && m.Type == StockMovementType.OutSales ? m.Quantity : 0m),
                         Returns = g.Sum(m => (!hasStart || m.Date >= start) && (m.ReferenceNumber.StartsWith("SRET-") || m.ReferenceNumber.StartsWith("PRET-")) ? m.Quantity : 0m),
                     };
        return from p in ProductsQuery(q)
               join t in totals on p.Id equals t.ItemId into matched
               from t in matched.DefaultIfEmpty()
               select new { p, Opening = (decimal?)t.Opening ?? 0m, Net = (decimal?)t.Net ?? 0m, t } into r
               select new ItemMovementSummaryRowDto
               {
                   ItemId = r.p.Id, Sku = r.p.Sku, ItemName = r.p.NameAr, Category = r.p.Category, Unit = r.p.Unit,
                   OpeningStock = r.Opening,
                   PurchaseInQty = (decimal?)r.t.PurchaseIn ?? 0m, SalesOutQty = (decimal?)r.t.SalesOut ?? 0m, ReturnsQty = (decimal?)r.t.Returns ?? 0m,
                   ClosingStock = r.Opening + r.Net,
                   AverageCost = r.p.AverageCost, TotalStockValue = Math.Round((r.Opening + r.Net) * r.p.AverageCost, 2),
               };
    }

    /// <summary>
    /// بطاقة الصنف: حركات الفترة، والرصيد الجاري (كمية وقيمة) لكل حركة يُحسب في قاعدة البيانات: مجموع كل ما سبقها
    /// (ومنه ما قبل الفترة) بترتيب التاريخ ثم وقت التسجيل.
    /// </summary>
    private IQueryable<DetailedItemLedgerEntryDto> ItemLedgerQuery(ReportQueryDto q)
    {
        if (!q.ItemId.HasValue) throw new ValidationFailedException(Messages.SelectItemForItemCard);
        var all = SignedMovements(q).Where(m => m.ItemId == q.ItemId && m.Sign != 0);
        var period = all;
        if (q.DateFrom.HasValue) period = period.Where(m => m.Date >= q.DateFrom);
        if (q.DateTo.HasValue) period = period.Where(m => m.Date <= q.DateTo);

        return period.Select(m => new DetailedItemLedgerEntryDto
        {
            Id = m.Id, ItemId = m.ItemId, Date = m.Date, CreatedAt = m.CreatedAt, DocType = m.Type.ToString(), DocNumber = m.ReferenceNumber,
            QuantityIn = m.Sign > 0 ? m.Quantity : 0m, QuantityOut = m.Sign < 0 ? m.Quantity : 0m,
            UnitCost = m.UnitCost, UnitPrice = m.UnitPrice, TotalValue = Math.Round(m.Quantity * m.UnitCost, 2),
            RunningStockBalance = all
                .Where(p => p.Date < m.Date || (p.Date == m.Date && (p.CreatedAt < m.CreatedAt || (p.CreatedAt == m.CreatedAt && p.Id.CompareTo(m.Id) <= 0))))
                .Sum(p => p.Sign * p.Quantity),
            RunningStockValue = Math.Round(all
                .Where(p => p.Date < m.Date || (p.Date == m.Date && (p.CreatedAt < m.CreatedAt || (p.CreatedAt == m.CreatedAt && p.Id.CompareTo(m.Id) <= 0))))
                .Sum(p => p.Sign * p.Quantity * p.UnitCost), 2),
        });
    }

    private IQueryable<CommercialTradeRowDto> CommercialTradeQuery(ReportQueryDto q)
    {
        var inv = _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == "posted");
        if (q.DateFrom.HasValue) inv = inv.Where(i => i.IssueDate >= q.DateFrom);
        if (q.DateTo.HasValue) inv = inv.Where(i => i.IssueDate <= q.DateTo);
        return inv.Select(i => new CommercialTradeRowDto
        {
            Id = i.Id, DocNumber = i.InvoiceNumber, Date = i.IssueDate, Type = i.Kind, PartyName = i.PartyName, VatNumber = i.PartyVatNumber,
            ItemsCount = i.Items.Count, Subtotal = i.Subtotal, DiscountTotal = i.DiscountTotal, VatTotal = i.VatTotal, GrandTotal = i.GrandTotal,
            CogsTotal = i.TotalCost, GrossProfit = i.GrossProfit,
            GrossProfitMarginPercent = i.Subtotal == 0 ? 0m : Math.Round(i.GrossProfit / i.Subtotal * 100, 2),
            ZatcaStatus = i.ZatcaStatus,
        });
    }

    public Task<LoadResult> LoadInventoryAuditAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(InventoryAuditQuery(q), options, nameof(InventoryAuditRowDto.ItemId), ct, EntityLoader.Asc(nameof(InventoryAuditRowDto.Sku)));
    public Task<LoadResult> LoadItemMovementsAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(ItemMovementsQuery(q), options, nameof(ItemMovementSummaryRowDto.ItemId), ct, EntityLoader.Asc(nameof(ItemMovementSummaryRowDto.Sku)));
    public Task<LoadResult> LoadItemLedgerAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(ItemLedgerQuery(q), options, nameof(DetailedItemLedgerEntryDto.Id), ct,
            EntityLoader.Asc(nameof(DetailedItemLedgerEntryDto.Date)), EntityLoader.Asc(nameof(DetailedItemLedgerEntryDto.CreatedAt)));
    public Task<LoadResult> LoadCommercialTradeAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(CommercialTradeQuery(q), options, nameof(CommercialTradeRowDto.Id), ct, EntityLoader.Asc(nameof(CommercialTradeRowDto.Date)));
}
