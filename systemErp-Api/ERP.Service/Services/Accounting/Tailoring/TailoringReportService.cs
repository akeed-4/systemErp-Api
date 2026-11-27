using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using D = ERP.Core.Models.Accounting.TailoringDeliveryStatuses;
using K = ERP.Core.Models.Accounting.TailoringMaterialKinds;
using St = ERP.Core.Models.Accounting.TailoringOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تقارير الخياطة الاثنا عشر بجدول واحد (أعمدة وصفوف وإجماليات من الخادم) في فترة (الافتراضي أول الشهر حتى اليوم). الربحية وأجور
/// الخياطين تتطلبان رؤية التكلفة، وعمود تكلفة المواد يُخفى بدونها. حد الصفوف 2000.
/// </summary>
public class TailoringReportService : ITailoringReportService
{
    public static readonly string[] Keys =
    {
        "orders", "late", "profitability", "materials", "customer_fabric", "wages", "productivity", "stage_durations", "deliveries", "advances",
        "garment_sales", "fittings",
    };
    private const int MaxRows = 2000;
    private const string OrderStatus = "tlr.st.", DeliveryStatus = "tlr.dlv.st.", FittingCode = "tlr.fit.";

    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public TailoringReportService(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public async Task<TailoringReportDto> ReportAsync(string key, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        if (!Keys.Contains(key)) throw new NotFoundException(Messages.TailoringReportNotFound);
        var today = SaudiTime.Today;
        var start = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
        var end = (to ?? today).Date;
        if (end < start) throw new ValidationFailedException(Messages.TailoringReportPeriodInvalid);
        var next = end.AddDays(1);
        var r = new TailoringReportDto { Key = key, From = start, To = end };
        var cost = await _permissions.HasPermissionAsync(PermissionCatalog.TailoringCostViewScreenId, ScreenAction.View, ct);
        if (key is "profitability" or "wages" && !cost) await _permissions.DemandAsync(PermissionCatalog.TailoringCostViewScreenId, ScreenAction.View, null, ct);

        switch (key)
        {
            case "orders":
            {
                Cols(r, ("orderNumber", "text"), ("orderDate", "date"), ("customerName", "text"), Code("status", OrderStatus), ("pieceCount", "number*"),
                    ("grandTotal", "money*"), ("advanceAmount", "money*"), ("deliveredTotal", "money*"), ("remaining", "money*"));
                var delivered = Delivered();
                var rows = await _db.Set<TailoringOrder>().AsNoTracking().Where(o => o.Status != St.Draft && o.OrderDate >= start && o.OrderDate < next)
                    .OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber).Take(MaxRows)
                    .Select(o => new { o.OrderNumber, o.OrderDate, o.CustomerName, o.Status, o.PieceCount, o.GrandTotal, o.AdvanceAmount,
                        Delivered = delivered.Where(d => d.OrderId == o.Id).Sum(d => (decimal?)d.TotalAmount) ?? 0 }).ToListAsync(ct);
                foreach (var o in rows)
                    Row(r, ("orderNumber", o.OrderNumber), ("orderDate", o.OrderDate), ("customerName", o.CustomerName), ("status", o.Status), ("pieceCount", o.PieceCount),
                        ("grandTotal", o.GrandTotal), ("advanceAmount", o.AdvanceAmount), ("deliveredTotal", o.Delivered),
                        ("remaining", o.Status == St.Cancelled ? 0m : o.GrandTotal - o.AdvanceAmount - o.Delivered));
                break;
            }
            case "late":
            {
                Cols(r, ("orderNumber", "text"), ("customerName", "text"), ("customerPhone", "text"), ("dueDate", "date"), ("daysLate", "number"),
                    Code("status", OrderStatus), ("readyPieces", "number*"), ("pieceCount", "number*"));
                var rows = await _db.Set<TailoringOrder>().AsNoTracking().Where(o => St.Open.Contains(o.Status) && o.ExpectedDeliveryDate != null && o.ExpectedDeliveryDate < today)
                    .OrderBy(o => o.ExpectedDeliveryDate).Take(MaxRows)
                    .Select(o => new { o.OrderNumber, o.CustomerName, o.CustomerPhone, Due = o.ExpectedDeliveryDate!.Value, o.Status, o.PieceCount,
                        Ready = _db.Set<TailoringGarment>().Count(g => g.OrderId == o.Id && g.Status == TailoringGarmentStatuses.Ready) }).ToListAsync(ct);
                foreach (var o in rows)
                    Row(r, ("orderNumber", o.OrderNumber), ("customerName", o.CustomerName), ("customerPhone", o.CustomerPhone), ("dueDate", o.Due),
                        ("daysLate", (today - o.Due.Date).Days), ("status", o.Status), ("readyPieces", o.Ready), ("pieceCount", o.PieceCount));
                break;
            }
            case "profitability":
            {
                Cols(r, ("orderNumber", "text"), ("customerName", "text"), Code("status", OrderStatus), ("revenue", "money*"), ("materialsCost", "money*"),
                    ("laborCost", "money*"), ("totalCost", "money*"), ("profit", "money*"), ("margin", "percent"));
                var delivered = Delivered();
                var ids = await delivered.Where(d => d.Date >= start && d.Date < next).Select(d => d.OrderId).Distinct().Take(MaxRows).ToListAsync(ct);
                var rows = await _db.Set<TailoringOrder>().AsNoTracking().Where(o => ids.Contains(o.Id)).OrderBy(o => o.OrderNumber)
                    .Select(o => new
                    {
                        o.OrderNumber, o.CustomerName, o.Status,
                        Revenue = delivered.Where(d => d.OrderId == o.Id).Sum(d => (decimal?)d.NetAmount) ?? 0,
                        Materials = _db.Set<TailoringMaterialIssue>().Where(m => m.OrderId == o.Id).Sum(m => (decimal?)(m.Kind == K.Issue ? m.TotalCost : -m.TotalCost)) ?? 0,
                        Labor = _db.Set<TailoringLaborEntry>().Where(l => l.OrderId == o.Id && l.Status == TailoringLaborStatuses.Posted).Sum(l => (decimal?)l.Amount) ?? 0,
                    }).ToListAsync(ct);
                foreach (var o in rows)
                {
                    var total = o.Materials + o.Labor;
                    Row(r, ("orderNumber", o.OrderNumber), ("customerName", o.CustomerName), ("status", o.Status), ("revenue", o.Revenue), ("materialsCost", o.Materials),
                        ("laborCost", o.Labor), ("totalCost", total), ("profit", o.Revenue - total), ("margin", Margin(o.Revenue - total, o.Revenue)));
                }
                r.Totals["margin"] = Margin(r.Totals.GetValueOrDefault("profit"), r.Totals.GetValueOrDefault("revenue"));
                break;
            }
            case "materials":
            {
                Cols(r, ("productName", "text"), ("unit", "text"), ("issued", "number*"), ("returned", "number*"), ("net", "number*"));
                if (cost) Cols(r, ("netCost", "money*"));
                var rows = await (from m in _db.Set<TailoringMaterialIssue>().AsNoTracking()
                                  join p in _db.Set<Product>().AsNoTracking() on m.ProductId equals p.Id
                                  where m.Date >= start && m.Date < next
                                  group m by new { p.NameAr, p.Unit } into g
                                  orderby g.Key.NameAr
                                  select new
                                  {
                                      g.Key.NameAr, g.Key.Unit,
                                      Issued = g.Sum(x => x.Kind == K.Issue ? x.Quantity : 0), Returned = g.Sum(x => x.Kind == K.Return ? x.Quantity : 0),
                                      Cost = g.Sum(x => x.Kind == K.Issue ? x.TotalCost : -x.TotalCost),
                                  }).Take(MaxRows).ToListAsync(ct);
                foreach (var m in rows)
                    Row(r, ("productName", m.NameAr), ("unit", m.Unit), ("issued", m.Issued), ("returned", m.Returned), ("net", m.Issued - m.Returned), ("netCost", cost ? m.Cost : null));
                break;
            }
            case "customer_fabric":
            {
                Cols(r, ("orderNumber", "text"), ("orderDate", "date"), ("customerName", "text"), ("garmentTypeName", "text"), ("description", "text"),
                    ("color", "text"), ("meters", "number*"), ("pieces", "number*"), Code("status", OrderStatus));
                var rows = await (from l in _db.Set<TailoringOrderLine>().AsNoTracking()
                                  join o in _db.Set<TailoringOrder>().AsNoTracking() on l.OrderId equals o.Id
                                  where l.FabricSource == TailoringFabricSources.Customer && o.Status != St.Draft && o.OrderDate >= start && o.OrderDate < next
                                  orderby o.OrderDate, o.OrderNumber, l.SortOrder
                                  select new { o.OrderNumber, o.OrderDate, o.CustomerName, l.GarmentTypeName, l.CustomerFabricDescription, l.CustomerFabricColor,
                                      l.CustomerFabricMeters, l.Quantity, o.Status }).Take(MaxRows).ToListAsync(ct);
                foreach (var x in rows)
                    Row(r, ("orderNumber", x.OrderNumber), ("orderDate", x.OrderDate), ("customerName", x.CustomerName), ("garmentTypeName", x.GarmentTypeName),
                        ("description", x.CustomerFabricDescription), ("color", x.CustomerFabricColor), ("meters", x.CustomerFabricMeters ?? 0), ("pieces", x.Quantity),
                        ("status", x.Status));
                break;
            }
            case "wages":
            {
                Cols(r, ("employeeName", "text"), ("pieces", "number*"), ("entries", "number*"), ("manualEntries", "number*"), ("amount", "money*"));
                var rows = await (from l in _db.Set<TailoringLaborEntry>().AsNoTracking()
                                  join e in _db.Set<Employee>().AsNoTracking() on l.EmployeeId equals e.Id
                                  where l.Status == TailoringLaborStatuses.Posted && l.Date >= start && l.Date < next
                                  group l by new { e.Id, e.NameAr } into g
                                  orderby g.Key.NameAr
                                  select new { g.Key.NameAr, Pieces = g.Select(x => x.GarmentId).Distinct().Count(), Entries = g.Count(),
                                      Manual = g.Count(x => x.IsManual), Amount = g.Sum(x => x.Amount) }).Take(MaxRows).ToListAsync(ct);
                foreach (var x in rows)
                    Row(r, ("employeeName", x.NameAr), ("pieces", x.Pieces), ("entries", x.Entries), ("manualEntries", x.Manual), ("amount", x.Amount));
                break;
            }
            case "productivity":
            {
                Cols(r, ("employeeName", "text"), ("stageExits", "number*"), ("pieces", "number*"), ("totalHours", "number*"), ("avgMinutes", "number"));
                var rows = await (from h in StageExits(start, next)
                                  join e in _db.Set<Employee>().AsNoTracking() on h.EmployeeId equals (Guid?)e.Id
                                  group h by new { e.Id, e.NameAr } into g
                                  orderby g.Count() descending
                                  select new { g.Key.NameAr, Exits = g.Count(), Pieces = g.Select(x => x.GarmentId).Distinct().Count(), Minutes = g.Sum(x => x.DurationMinutes) })
                    .Take(MaxRows).ToListAsync(ct);
                foreach (var x in rows)
                    Row(r, ("employeeName", x.NameAr), ("stageExits", x.Exits), ("pieces", x.Pieces), ("totalHours", decimal.Round(x.Minutes / 60m, 1)),
                        ("avgMinutes", x.Exits > 0 ? x.Minutes / x.Exits : 0));
                break;
            }
            case "stage_durations":
            {
                Cols(r, ("stageName", "text"), ("stageExits", "number*"), ("avgMinutes", "number"), ("maxMinutes", "number"), ("totalHours", "number*"));
                var rows = await (from h in _db.Set<TailoringStageHistory>().AsNoTracking()
                                  join s in _db.Set<TailoringStage>().AsNoTracking() on h.FromStageId equals (Guid?)s.Id
                                  where h.ChangedAt >= start && h.ChangedAt < next
                                  group h by new { s.Id, s.NameAr, s.SortOrder } into g
                                  orderby g.Key.SortOrder
                                  select new { g.Key.NameAr, Exits = g.Count(), Minutes = g.Sum(x => x.DurationMinutes), Max = g.Max(x => x.DurationMinutes) })
                    .Take(MaxRows).ToListAsync(ct);
                foreach (var x in rows)
                    Row(r, ("stageName", x.NameAr), ("stageExits", x.Exits), ("avgMinutes", x.Exits > 0 ? x.Minutes / x.Exits : 0), ("maxMinutes", x.Max),
                        ("totalHours", decimal.Round(x.Minutes / 60m, 1)));
                break;
            }
            case "deliveries":
            {
                Cols(r, ("deliveryNumber", "text"), ("date", "date"), ("orderNumber", "text"), ("customerName", "text"), ("pieceCount", "number*"),
                    ("netAmount", "money*"), ("advanceRecovery", "money*"), ("vatAmount", "money*"), ("totalAmount", "money*"), ("invoiceNumber", "text"),
                    Code("status", DeliveryStatus));
                var rows = await (from d in _db.Set<TailoringDelivery>().AsNoTracking()
                                  join o in _db.Set<TailoringOrder>().AsNoTracking() on d.OrderId equals o.Id
                                  where d.Date >= start && d.Date < next
                                  orderby d.Date, d.DeliveryNumber
                                  select new { d.DeliveryNumber, d.Date, o.OrderNumber, o.CustomerName, d.PieceCount, d.NetAmount, d.AdvanceRecovery, d.VatAmount,
                                      d.TotalAmount, d.InvoiceNumber, d.Status }).Take(MaxRows).ToListAsync(ct);
                foreach (var d in rows)
                {
                    var on = d.Status == D.Posted; // الملغى يُعرض ولا يدخل الإجمالي
                    Row(r, ("deliveryNumber", d.DeliveryNumber), ("date", d.Date), ("orderNumber", d.OrderNumber), ("customerName", d.CustomerName),
                        ("pieceCount", on ? d.PieceCount : 0), ("netAmount", on ? d.NetAmount : 0m), ("advanceRecovery", on ? d.AdvanceRecovery : 0m),
                        ("vatAmount", on ? d.VatAmount : 0m), ("totalAmount", on ? d.TotalAmount : 0m), ("invoiceNumber", d.InvoiceNumber), ("status", d.Status));
                }
                break;
            }
            case "advances":
            {
                Cols(r, ("orderNumber", "text"), ("customerName", "text"), ("advanceInvoiceNumber", "text"), ("advanceAt", "date"), ("advanceAmount", "money*"),
                    ("advanceNet", "money*"), ("advanceRecovered", "money*"), ("outstanding", "money*"), Code("status", OrderStatus));
                var rows = await _db.Set<TailoringOrder>().AsNoTracking()
                    .Where(o => o.AdvanceInvoiceId != null && o.AdvanceAt >= start && o.AdvanceAt < next).OrderBy(o => o.AdvanceAt).Take(MaxRows)
                    .Select(o => new { o.OrderNumber, o.CustomerName, o.AdvanceInvoiceNumber, o.AdvanceAt, o.AdvanceAmount, o.AdvanceNet, o.AdvanceRecovered, o.Status })
                    .ToListAsync(ct);
                foreach (var o in rows)
                    Row(r, ("orderNumber", o.OrderNumber), ("customerName", o.CustomerName), ("advanceInvoiceNumber", o.AdvanceInvoiceNumber), ("advanceAt", o.AdvanceAt),
                        ("advanceAmount", o.AdvanceAmount), ("advanceNet", o.AdvanceNet), ("advanceRecovered", o.AdvanceRecovered),
                        ("outstanding", o.AdvanceNet - o.AdvanceRecovered), ("status", o.Status));
                break;
            }
            case "garment_sales":
            {
                Cols(r, ("garmentTypeName", "text"), ("pieces", "number*"), ("netAmount", "money*"), ("avgPrice", "money"));
                var rows = await (from p in _db.Set<TailoringDeliveryPiece>().AsNoTracking()
                                  join d in _db.Set<TailoringDelivery>().AsNoTracking() on p.DeliveryId equals d.Id
                                  join l in _db.Set<TailoringOrderLine>().AsNoTracking() on p.LineId equals l.Id
                                  where d.Status == D.Posted && d.Date >= start && d.Date < next
                                  group p by l.GarmentTypeName into g
                                  orderby g.Sum(x => x.NetAmount) descending
                                  select new { Name = g.Key, Pieces = g.Count(), Net = g.Sum(x => x.NetAmount) }).Take(MaxRows).ToListAsync(ct);
                foreach (var x in rows)
                    Row(r, ("garmentTypeName", x.Name), ("pieces", x.Pieces), ("netAmount", x.Net), ("avgPrice", x.Pieces > 0 ? decimal.Round(x.Net / x.Pieces, 2) : 0m));
                break;
            }
            case "fittings":
            {
                Cols(r, ("scheduledAt", "datetime"), ("orderNumber", "text"), ("customerName", "text"), ("garmentCode", "text"), Code("status", FittingCode),
                    Code("result", FittingCode), ("notes", "text"));
                var rows = await (from f in _db.Set<TailoringFitting>().AsNoTracking()
                                  join o in _db.Set<TailoringOrder>().AsNoTracking() on f.OrderId equals o.Id
                                  join g in _db.Set<TailoringGarment>().AsNoTracking() on f.GarmentId equals g.Id
                                  where f.ScheduledAt >= start && f.ScheduledAt < next
                                  orderby f.ScheduledAt
                                  select new { f.ScheduledAt, o.OrderNumber, o.CustomerName, g.Code, f.Status, f.Result, f.Notes }).Take(MaxRows).ToListAsync(ct);
                foreach (var f in rows)
                    Row(r, ("scheduledAt", f.ScheduledAt), ("orderNumber", f.OrderNumber), ("customerName", f.CustomerName), ("garmentCode", f.Code),
                        ("status", f.Status), ("result", f.Result), ("notes", f.Notes));
                break;
            }
        }
        return r;
    }

    // ---------- مساعدات ----------

    private IQueryable<TailoringDelivery> Delivered() => _db.Set<TailoringDelivery>().AsNoTracking().Where(d => d.Status == D.Posted);

    /// <summary>خروج القطع من مراحلها بمنفّذها في الفترة (أساس الإنتاجية).</summary>
    private IQueryable<TailoringStageHistory> StageExits(DateTime start, DateTime next)
        => _db.Set<TailoringStageHistory>().AsNoTracking().Where(h => h.FromStageId != null && h.EmployeeId != null && h.ChangedAt >= start && h.ChangedAt < next);

    private static decimal Margin(decimal profit, decimal revenue) => revenue != 0 ? decimal.Round(profit / revenue * 100m, 2) : 0;

    private static (string, string) Code(string key, string prefix) => (key, "code:" + prefix);

    /// <summary>النوع بنجمة = يُجمع؛ «code:بادئة» = رمز يُترجم.</summary>
    private static void Cols(TailoringReportDto r, params (string Key, string Type)[] cols)
    {
        foreach (var (key, type) in cols)
        {
            var total = type.EndsWith('*');
            var t = total ? type[..^1] : type;
            r.Columns.Add(t.StartsWith("code:", StringComparison.Ordinal)
                ? new TailoringReportColumnDto { Key = key, Type = "code", Prefix = t[5..] }
                : new TailoringReportColumnDto { Key = key, Type = t, Total = total });
        }
    }

    private static void Row(TailoringReportDto r, params (string Key, object? Value)[] values)
    {
        var row = new Dictionary<string, object?>();
        foreach (var (key, value) in values)
        {
            row[key] = value;
            if (r.Columns.FirstOrDefault(c => c.Key == key) is { Total: true })
                r.Totals[key] = r.Totals.GetValueOrDefault(key) + Convert.ToDecimal(value ?? 0m);
        }
        r.Rows.Add(row);
    }
}
