using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.CarShowroom;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// منطق أسطر السيارات داخل فواتير الشراء/البيع، يستدعيه InvoiceService عند البناء والترحيل وإلغاء الترحيل.
/// لا يحسب قيودًا بنفسه: يشتق InvoiceItem لكل سيارة فيمرّ الأثر المحاسبي عبر محرك الفوترة والترحيل المركزي.
/// إنشاء المركبات يمرّ عبر IVehicleService.CreateAsync (نفس تحقق VIN والماركة/الموديل/الفئة والتسعير).
/// </summary>
internal static class VehicleInvoiceLines
{
    private const decimal VatRate = 15m;

    /// <summary>يتحقق من أسطر السيارات ويحسب أرقامها ويستبدل r.Items ببنود مشتقة. لا أثر جانبي على قاعدة البيانات.</summary>
    public static async Task<List<InvoiceVehicleLine>> ResolveAsync(ErpDbContext db, CreateInvoiceDto r, CancellationToken ct)
    {
        var isSales = r.Kind == InvoiceKind.Sales;
        if (!isSales && r.Kind != InvoiceKind.Purchase)
            throw new ValidationFailedException("أسطر السيارات مدعومة لفواتير الشراء والبيع فقط.");
        if (r.Items.Count > 0) throw new ValidationFailedException("لا تخلط بنود الأصناف مع أسطر السيارات في فاتورة واحدة.");
        if (r.InvoiceDiscount != 0) throw new ValidationFailedException("خصم الفاتورة غير مدعوم مع أسطر السيارات؛ استخدم خصم السطر.");

        var draft = string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase);
        var errors = new List<string>();
        var seenVins = new HashSet<string>();
        var seenVehicles = new HashSet<Guid>();

        var vehicleIds = r.VehicleLines.Where(l => l.VehicleId.HasValue).Select(l => l.VehicleId!.Value).Distinct().ToList();
        var vehicles = await db.Set<Vehicle>().AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

        var lines = new List<InvoiceVehicleLine>();
        for (var i = 0; i < r.VehicleLines.Count; i++)
        {
            var d = r.VehicleLines[i];
            var n = i + 1;
            var line = new InvoiceVehicleLine
            {
                LineNo = n, VehicleId = d.VehicleId, TempRef = string.IsNullOrWhiteSpace(d.TempRef) ? null : d.TempRef.Trim(),
                BrandId = d.BrandId, ModelId = d.ModelId, TrimId = d.TrimId,
                BrandNameAr = (d.BrandNameAr ?? string.Empty).Trim(), ModelNameAr = (d.ModelNameAr ?? string.Empty).Trim(), TrimNameAr = d.TrimNameAr?.Trim(),
                Year = d.Year, ColorExterior = (d.ColorExterior ?? string.Empty).Trim(), ColorInterior = (d.ColorInterior ?? string.Empty).Trim(),
                EngineNumber = d.EngineNumber?.Trim(), CustomsCardNumber = d.CustomsCardNumber?.Trim(),
                VatMode = d.VatMode, UnitPrice = d.UnitPrice, Discount = d.Discount,
            };

            if (d.UnitPrice < 0 || d.Discount < 0 || d.Discount > d.UnitPrice) errors.Add($"السطر {n}: السعر والخصم غير صالحين.");
            var vin = (d.Vin ?? string.Empty).Trim().ToUpperInvariant();

            if (isSales)
            {
                if (!d.VehicleId.HasValue || !vehicles.TryGetValue(d.VehicleId.Value, out var v)) { errors.Add($"السطر {n}: اختر مركبة من المخزون المتاح."); continue; }
                if (!seenVehicles.Add(v.Id)) errors.Add($"السطر {n}: المركبة مكررة في نفس الفاتورة.");
                if (v.Status != VehicleStatus.Available) errors.Add($"السطر {n}: المركبة {v.ChassisNumber} غير متاحة للبيع.");
                line.Vin = v.ChassisNumber; line.BrandId = v.BrandId; line.ModelId = v.ModelId; line.TrimId = v.TrimId;
                line.BrandNameAr = v.BrandNameAr; line.ModelNameAr = v.ModelNameAr; line.TrimNameAr = v.TrimNameAr; line.Year = v.Year;
                line.ColorExterior = v.ColorExterior; line.ColorInterior = v.ColorInterior;
                line.EngineNumber = v.EngineNumber; line.CustomsCardNumber = v.CustomsCardNumber;
                line.UnitCost = v.TotalCost; line.VehicleId = v.Id;
                if (v.MinSellingPrice.HasValue && d.UnitPrice - d.Discount < v.MinSellingPrice)
                    errors.Add($"السطر {n}: سعر البيع بعد الخصم أقل من الحد الأدنى للمركبة ({v.MinSellingPrice:0.00}).");
            }
            else
            {
                line.VehicleId = null;
                if (d.VatMode is not (VatMode.Standard_15 or VatMode.Exempt)) errors.Add($"السطر {n}: نمط ضريبة الشراء قياسي 15% أو معفى فقط.");
                if (string.IsNullOrWhiteSpace(line.BrandNameAr) || string.IsNullOrWhiteSpace(line.ModelNameAr)) errors.Add($"السطر {n}: الماركة والموديل مطلوبان.");
                if (d.Year is < 1980 or > 2100) errors.Add($"السطر {n}: سنة الصنع غير صالحة.");
                if (vin.Length == 0)
                {
                    if (!draft) errors.Add($"السطر {n}: رقم الشاسيه مطلوب للترحيل.");
                }
                else
                {
                    if (!VehicleService.VinPattern.IsMatch(vin)) errors.Add($"السطر {n}: رقم الشاسيه يجب أن يتكون من 17 خانة (أحرف وأرقام بدون I/O/Q).");
                    else if (!seenVins.Add(vin)) errors.Add($"السطر {n}: رقم الشاسيه {vin} مكرر في نفس الفاتورة.");
                    line.Vin = vin;
                }
            }

            var net = DocumentPricing.Round(d.UnitPrice - d.Discount);
            var vat = isSales
                ? CarVat.Calculate(line.UnitCost, net, d.VatMode).VatAmount
                : d.VatMode == VatMode.Standard_15 ? DocumentPricing.Round(net * VatRate / 100m) : 0m;
            line.TotalBeforeVat = net; line.VatAmount = vat; line.TotalAfterVat = net + vat;
            lines.Add(line);
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        // فحص التكرار مع المخزون الحالي (مسموح فقط بوجود نفس الفاتورة نفسها عند التعديل — الحذف قبل البناء يتكفل بذلك)
        if (!isSales)
        {
            var vins = lines.Where(l => l.Vin != null).Select(l => l.Vin!).ToList();
            if (vins.Count > 0)
            {
                var existing = await db.Set<Vehicle>().AsNoTracking().Where(v => vins.Contains(v.ChassisNumber)).Select(v => v.ChassisNumber).ToListAsync(ct);
                if (existing.Count > 0) throw new ConflictException("شواسيهات مسجّلة مسبقاً: " + string.Join(", ", existing));
            }
        }

        r.Items = lines.Select(l => new InvoiceItemDto
        {
            ItemId = Guid.Empty,
            ItemName = Describe(l),
            Unit = "سيارة", Quantity = 1, UnitPrice = l.UnitPrice, Discount = l.Discount,
            VatRate = l.VatMode == VatMode.Standard_15 ? VatRate : 0,
            VatAmountOverride = isSales ? l.VatAmount : null,
            UnitCost = isSales ? l.UnitCost : 0,
        }).ToList();
        return lines;
    }

    private static string Describe(InvoiceVehicleLine l)
    {
        var spec = $"{l.BrandNameAr} {l.ModelNameAr} {l.TrimNameAr} {l.Year}".Replace("  ", " ").Trim();
        return l.Vin != null ? $"{spec} (VIN: {l.Vin})" : l.TempRef != null ? $"{spec} ({l.TempRef})" : spec;
    }

    /// <summary>عند الترحيل: شراء ← إنشاء المركبات؛ بيع ← تحويلها Sold. يرمي أخطاء برقم السطر.</summary>
    public static async Task OnPostingAsync(ErpDbContext db, IVehicleService vehicles, Invoice invoice, CancellationToken ct)
    {
        if (invoice.VehicleLines.Count == 0) return;
        var lines = invoice.VehicleLines.OrderBy(l => l.LineNo).ToList();

        if (invoice.Kind == InvoiceKind.Purchase)
        {
            foreach (var l in lines.Where(l => l.VehicleId == null))
            {
                if (string.IsNullOrWhiteSpace(l.Vin)) throw new ValidationFailedException($"السطر {l.LineNo}: رقم الشاسيه مطلوب للترحيل.");
                try
                {
                    var created = await vehicles.CreateAsync(new CreateVehicleDto
                    {
                        ChassisNumber = l.Vin!, EngineNumber = l.EngineNumber, CustomsCardNumber = l.CustomsCardNumber,
                        BrandId = l.BrandId, ModelId = l.ModelId, TrimId = l.TrimId,
                        BrandNameAr = l.BrandNameAr, ModelNameAr = l.ModelNameAr, TrimNameAr = l.TrimNameAr, Trim = l.TrimNameAr,
                        Year = l.Year, ColorExterior = l.ColorExterior, ColorInterior = l.ColorInterior, Condition = VehicleCondition.New,
                        PurchasePrice = l.TotalBeforeVat, SellingPrice = l.TotalBeforeVat, VatMode = VatMode.Standard_15, Location = string.Empty,
                    }, ct);
                    var entity = await db.Set<Vehicle>().FirstAsync(v => v.Id == created.Id, ct);
                    entity.PurchaseInvoiceId = invoice.Id;
                    l.VehicleId = created.Id;
                }
                catch (ValidationFailedException ex) { throw new ValidationFailedException($"السطر {l.LineNo}: {ex.Message}"); }
                catch (ConflictException ex) { throw new ConflictException($"السطر {l.LineNo}: {ex.Message}"); }
            }
        }
        else if (invoice.Kind == InvoiceKind.Sales)
        {
            foreach (var l in lines)
            {
                var v = await db.Set<Vehicle>().FirstOrDefaultAsync(x => x.Id == l.VehicleId, ct)
                    ?? throw new ValidationFailedException($"السطر {l.LineNo}: المركبة غير موجودة.");
                if (v.Status != VehicleStatus.Available) throw new ConflictException($"السطر {l.LineNo}: المركبة {v.ChassisNumber} لم تعد متاحة للبيع.");
                v.Status = VehicleStatus.Sold;
            }
        }
    }

    /// <summary>عند إلغاء الترحيل: شراء ← حذف المركبات المنشأة إن بقيت متاحة؛ بيع ← إعادتها Available.</summary>
    public static async Task OnUnpostAsync(ErpDbContext db, Invoice invoice, CancellationToken ct)
    {
        if (invoice.VehicleLines.Count == 0) return;
        foreach (var l in invoice.VehicleLines.Where(l => l.VehicleId.HasValue))
        {
            var v = await db.Set<Vehicle>().FirstOrDefaultAsync(x => x.Id == l.VehicleId, ct);
            if (v == null) { l.VehicleId = null; continue; }

            if (invoice.Kind == InvoiceKind.Purchase)
            {
                var used = v.Status != VehicleStatus.Available
                    || await db.Set<CarSalesContract>().AnyAsync(c => c.VehicleId == v.Id, ct)
                    || await db.Set<InvoiceVehicleLine>().AnyAsync(x => x.VehicleId == v.Id && x.InvoiceId != invoice.Id, ct);
                if (used) throw new ConflictException($"لا يمكن إلغاء ترحيل الفاتورة: المركبة {v.ChassisNumber} لم تعد متاحة (محجوزة أو مباعة أو مرتبطة بمستند).");
                db.Remove(v);
                l.VehicleId = null;
            }
            else if (v.Status == VehicleStatus.Sold)
            {
                v.Status = VehicleStatus.Available;
            }
        }
    }
}
