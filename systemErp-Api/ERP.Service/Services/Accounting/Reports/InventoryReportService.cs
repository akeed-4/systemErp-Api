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

    private static void ValidatePeriod(ReportQueryDto q, bool includeStart = true)
    {
        if (includeStart && q.DateFrom.HasValue && q.DateTo.HasValue && q.DateFrom.Value.Date > q.DateTo.Value.Date)
            throw new ValidationFailedException("تاريخ البداية يجب أن يكون في أو قبل تاريخ النهاية.");
    }

    private sealed class SignedMovement
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }
        public Guid? WarehouseId { get; set; }
        public DateTime Date { get; set; }
        public DateTime CreatedAt { get; set; }
        public StockMovementType Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        /// <summary>ما غيّرته الحركة في قيمة الصنف الدفترية (StockMovement.ValueChange).</summary>
        public decimal ValueChange { get; set; }
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
            Id = m.Id, ItemId = m.ItemId, WarehouseId = m.WarehouseId, Date = m.Date, CreatedAt = m.CreatedAt, Type = m.Type, Quantity = m.Quantity,
            UnitCost = m.UnitCost, ValueChange = m.ValueChange, UnitPrice = m.UnitPrice, ReferenceNumber = m.ReferenceNumber,
            Sign = m.Type == StockMovementType.InPurchase || m.Type == StockMovementType.AdjustmentIn ? 1
                : m.Type == StockMovementType.TransferIn ? transfer
                : m.Type == StockMovementType.TransferOut ? -transfer
                : m.Type == StockMovementType.Revaluation ? 0
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
        // لمستودع بعينه: الكمية رصيده فيه، بالتكلفة الموحّدة للصنف.
        var stocks = _db.Set<WarehouseStock>().AsNoTracking().Where(s => s.WarehouseId == q.WarehouseId);
        var rows = q.WarehouseId.HasValue
            ? ProductsQuery(q).Select(p => new { p, Quantity = stocks.Where(s => s.ItemId == p.Id).Sum(s => s.Quantity), UnitCost = p.AverageCost })
            : ProductsQuery(q).Select(p => new { p, Quantity = p.CurrentStock, UnitCost = p.AverageCost });

        // الجرد لقطة رصيد حتى نهاية اليوم المحدد. نرجع صافي حركات ما بعده من الرصيد الحالي؛
        // تكلفة الوحدة التاريخية تُستعاد من تغيّر قيمة المخزون على مستوى المنشأة.
        if (q.EndExclusive() is { } end)
        {
            var laterForWarehouse = SignedMovements(q).Where(m => m.Date >= end)
                .GroupBy(m => m.ItemId)
                .Select(g => new { ItemId = g.Key, Quantity = g.Sum(m => m.Sign * m.Quantity) });
            var laterForCompany = SignedMovements(new ReportQueryDto()).Where(m => m.Date >= end)
                .GroupBy(m => m.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Quantity = g.Sum(m => m.Sign * m.Quantity),
                    Value = g.Sum(m => m.ValueChange),
                });

            rows = from r in rows
                   join w in laterForWarehouse on r.p.Id equals w.ItemId into warehouseChanges
                   from w in warehouseChanges.DefaultIfEmpty()
                   join c in laterForCompany on r.p.Id equals c.ItemId into companyChanges
                   from c in companyChanges.DefaultIfEmpty()
                   let historicalCompanyQuantity = r.p.CurrentStock - ((decimal?)c.Quantity ?? 0m)
                   let historicalCompanyValue = r.p.CurrentStock * r.p.AverageCost - ((decimal?)c.Value ?? 0m)
                   select new
                   {
                       p = r.p,
                       Quantity = r.Quantity - ((decimal?)w.Quantity ?? 0m),
                       UnitCost = historicalCompanyQuantity == 0 ? r.p.AverageCost : historicalCompanyValue / historicalCompanyQuantity,
                   };
        }

        return rows.Select(r => new InventoryAuditRowDto
        {
            ItemId = r.p.Id, Sku = r.p.Sku, Barcode = r.p.Barcode, ItemName = r.p.NameAr, Category = r.p.Category, Unit = r.p.Unit,
            SystemQuantity = r.Quantity, AverageUnitCost = r.UnitCost,
            SystemValuation = Math.Round(r.Quantity * r.UnitCost, 2),
        });
    }

    private IQueryable<ItemMovementSummaryRowDto> ItemMovementsQuery(ReportQueryDto q)
    {
        DateTime? start = q.DateFrom, end = q.EndExclusive();
        var hasStart = start.HasValue;
        var movements = SignedMovements(q);
        if (end.HasValue) movements = movements.Where(m => m.Date < end);

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
        // إعادة التقييم بلا كمية لكنها تغيّر القيمة: تظهر في البطاقة
        var all = SignedMovements(q).Where(m => m.ItemId == q.ItemId && (m.Sign != 0 || m.Type == StockMovementType.Revaluation));
        // قيمة الصنف دفترية على مستوى المنشأة: مجموع ما غيّرته كل حركة (ومنه إعادة تسعير الرصيد). ولمستودع بعينه لا قيمة دفترية مستقلة:
        // الكمية × تكلفة الحركة كما كانت
        var byWarehouse = q.WarehouseId.HasValue;
        var period = all;
        if (q.DateFrom.HasValue) period = period.Where(m => m.Date >= q.DateFrom);
        if (q.DateTo.HasValue) period = period.Where(m => m.Date < q.EndExclusive());

        period = q.MovementType switch
        {
            "sales_return" => period.Where(m => m.ReferenceNumber.StartsWith("SRET-")),
            "purchase_return" => period.Where(m => m.ReferenceNumber.StartsWith("PRET-")),
            "purchase" => period.Where(m => m.Type == StockMovementType.InPurchase && !m.ReferenceNumber.StartsWith("SRET-") && !m.ReferenceNumber.StartsWith("PRET-")),
            "sales" => period.Where(m => m.Type == StockMovementType.OutSales && !m.ReferenceNumber.StartsWith("SRET-") && !m.ReferenceNumber.StartsWith("PRET-")),
            "adjustment" => period.Where(m => m.Type == StockMovementType.AdjustmentIn || m.Type == StockMovementType.AdjustmentOut),
            "transfer" => period.Where(m => m.Type == StockMovementType.TransferIn || m.Type == StockMovementType.TransferOut),
            "revaluation" => period.Where(m => m.Type == StockMovementType.Revaluation),
            _ => period,
        };
        var warehouses = _db.Set<Warehouse>().AsNoTracking();

        return period.Select(m => new DetailedItemLedgerEntryDto
        {
            Id = m.Id, ItemId = m.ItemId, Date = m.Date, CreatedAt = m.CreatedAt, DocNumber = m.ReferenceNumber,
            // المرتجعات تُعرف ببادئة رقم مستندها (كما في ملخص حركة الأصناف)
            DocType = m.ReferenceNumber.StartsWith("SRET-") ? "sales_return"
                : m.ReferenceNumber.StartsWith("PRET-") ? "purchase_return"
                : m.Type == StockMovementType.InPurchase ? "purchase"
                : m.Type == StockMovementType.OutSales ? "sales"
                : m.Type == StockMovementType.AdjustmentIn ? "adjustment_in"
                : m.Type == StockMovementType.AdjustmentOut ? "adjustment_out"
                : m.Type == StockMovementType.TransferIn ? "transfer_in"
                : m.Type == StockMovementType.TransferOut ? "transfer_out" : "revaluation",
            WarehouseId = m.WarehouseId,
            WarehouseName = warehouses.Where(w => w.Id == m.WarehouseId).Select(w => w.NameAr).FirstOrDefault(),
            QuantityIn = m.Sign > 0 ? m.Quantity : 0m, QuantityOut = m.Sign < 0 ? m.Quantity : 0m,
            UnitCost = m.UnitCost, UnitPrice = m.UnitPrice,
            TotalValue = byWarehouse ? Math.Round(m.Quantity * m.UnitCost, 2) : Math.Abs(m.ValueChange),
            RunningStockBalance = all
                .Where(p => p.Date < m.Date || (p.Date == m.Date && (p.CreatedAt < m.CreatedAt || (p.CreatedAt == m.CreatedAt && p.Id.CompareTo(m.Id) <= 0))))
                .Sum(p => p.Sign * p.Quantity),
            RunningStockValue = Math.Round(all
                .Where(p => p.Date < m.Date || (p.Date == m.Date && (p.CreatedAt < m.CreatedAt || (p.CreatedAt == m.CreatedAt && p.Id.CompareTo(m.Id) <= 0))))
                .Sum(p => byWarehouse ? p.Sign * p.Quantity * p.UnitCost : p.ValueChange), 2),
        });
    }

    private IQueryable<CommercialTradeRowDto> CommercialTradeQuery(ReportQueryDto q)
    {
        var inv = _db.Set<InvoiceBase>().AsNoTracking().Where(i => i.Status == "posted");
        var items = _db.Set<InvoiceItemBase>().AsNoTracking(); // بنود كل جداول الفواتير (البند يشير إلى رأس جدوله)
        if (q.DateFrom.HasValue) inv = inv.Where(i => i.IssueDate >= q.DateFrom);
        if (q.DateTo.HasValue) inv = inv.Where(i => i.IssueDate < q.EndExclusive());
        if (q.WarehouseId.HasValue) inv = inv.Where(i => i.WarehouseId == q.WarehouseId.Value);
        if (!string.IsNullOrWhiteSpace(q.DocType) && Enum.TryParse<InvoiceKind>(q.DocType.Replace("_", ""), true, out var kind))
            inv = inv.Where(i => i.Kind == kind);
        var moduleScope = ReportModuleScope.Normalize(q.Module);
        if (moduleScope != null) inv = inv.Where(i => moduleScope.Contains(i.Module));
        var users = _db.Set<User>().AsNoTracking();
        // كل فاتورة مرحّلة تدخل كما في القيد، مرتبطة بالهيئة أم لا: الإيراد صافيها قبل الضريبة والضريبة عمود مستقل.
        // المرتجع بالسالب في كل المبالغ (مجمل الربح مخزّن بإشارته أصلاً) فتصح إجماليات الأعمدة.
        // نسبة الربح على التكلفة؛ ومندوب المبيعات المسند إليه المستند وإلا منشئه.
        return from i in inv
               let s = i.IsReturn || i.Kind == InvoiceKind.SalesReturn || i.Kind == InvoiceKind.PurchaseReturn ? -1m : 1m
               select new CommercialTradeRowDto
               {
                   Id = i.Id, DocNumber = i.InvoiceNumber, Date = i.IssueDate, Type = i.Kind, PartyName = i.PartyName, VatNumber = i.PartyVatNumber,
                   ItemsCount = items.Count(it => it.InvoiceId == i.Id),
                   GrossTotal = s * (i.Subtotal + i.DiscountTotal),
                   InvoiceDiscount = s * i.InvoiceDiscount,
                   ItemsDiscountTotal = s * i.ItemsDiscountTotal,
                   DiscountTotal = s * i.DiscountTotal,
                   Subtotal = s * i.Subtotal,
                   ExciseTaxTotal = s * i.ExciseTaxTotal,
                   AmountAfterOtherTaxes = s * (i.Subtotal + i.ExciseTaxTotal),
                   VatTotal = s * i.VatTotal,
                   GrandTotal = s * i.GrandTotal,
                   CurrencyCode = i.CurrencyCode, ExchangeRate = i.ExchangeRate,
                   CogsTotal = s * i.TotalCost, GrossProfit = i.GrossProfit,
                   GrossProfitMarginPercent = i.Subtotal == 0 ? 0m : Math.Round(i.GrossProfit / i.Subtotal * 100, 2),
                   ProfitPercent = i.TotalCost == 0 ? 0m : Math.Round(i.GrossProfit / i.TotalCost * 100, 2),
                   SalesRepName = users.Where(u => u.Id == (i.AssignedToUserId ?? i.CreatedByUserId)).Select(u => u.Name).FirstOrDefault(),
                   ZatcaStatus = i.ZatcaStatus,
               };
    }

    public Task<LoadResult> LoadInventoryAuditAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
    {
        ValidatePeriod(q, includeStart: false);
        return ReportLoader.LoadAsync(InventoryAuditQuery(q), options, nameof(InventoryAuditRowDto.ItemId), ct, EntityLoader.Asc(nameof(InventoryAuditRowDto.Sku)));
    }

    public Task<LoadResult> LoadItemMovementsAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
    {
        ValidatePeriod(q);
        return ReportLoader.LoadAsync(ItemMovementsQuery(q), options, nameof(ItemMovementSummaryRowDto.ItemId), ct, EntityLoader.Asc(nameof(ItemMovementSummaryRowDto.Sku)));
    }

    public Task<LoadResult> LoadItemLedgerAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
    {
        ValidatePeriod(q);
        return ReportLoader.LoadAsync(ItemLedgerQuery(q), options, nameof(DetailedItemLedgerEntryDto.Id), ct,
            EntityLoader.Asc(nameof(DetailedItemLedgerEntryDto.Date)), EntityLoader.Asc(nameof(DetailedItemLedgerEntryDto.CreatedAt)));
    }

    public Task<LoadResult> LoadCommercialTradeAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
    {
        ValidatePeriod(q);
        return ReportLoader.LoadAsync(CommercialTradeQuery(q), options, nameof(CommercialTradeRowDto.Id), ct, EntityLoader.Asc(nameof(CommercialTradeRowDto.Date)));
    }
}
