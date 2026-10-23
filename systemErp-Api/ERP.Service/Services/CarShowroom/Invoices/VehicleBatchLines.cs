using System.Text.RegularExpressions;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>تحقق جماعي من أرقام الشاسيه: الصيغة، التكرار داخل القائمة، والوجود في المنشأة باستعلام واحد.</summary>
internal static class ChassisCheck
{
    /// <summary>رقم مؤقت ولّده النظام (TMP + 14 رقماً): صيغته صيغة VIN ليمرّ في كل المسارات، ويُميَّز بهذا النمط وبعلامة IsTemporaryChassis.</summary>
    private static readonly Regex Provisional = new($"^{VehicleService.ProvisionalVinPrefix}[0-9]{{14}}$", RegexOptions.Compiled);

    public static bool IsProvisional(string? vin) => vin != null && Provisional.IsMatch(vin);
    public static string Normalize(string? vin) => (vin ?? string.Empty).Trim().ToUpperInvariant();

    /// <param name="vins">أرقام مطبَّعة (Normalize) بترتيب الإدخال.</param>
    /// <param name="draftInvoiceId">مسودة الفاتورة الجاري حفظها: بطاقاتها المسودة لا تُعدّ تكراراً.</param>
    public static async Task<List<ChassisIssueDto>> RunAsync(ErpDbContext db, IReadOnlyList<string> vins, Guid? draftInvoiceId, CancellationToken ct)
    {
        var issues = new List<ChassisIssueDto>();
        var seen = new HashSet<string>();
        var candidates = new List<int>();
        for (var i = 0; i < vins.Count; i++)
        {
            var vin = vins[i];
            if (vin.Length == 0) issues.Add(new ChassisIssueDto { Index = i, Vin = vin, Code = "required", Message = Messages.ChassisRequired });
            else if (!VehicleService.VinPattern.IsMatch(vin)) issues.Add(new ChassisIssueDto { Index = i, Vin = vin, Code = "format", Message = Messages.VinFormatInvalid });
            else if (!seen.Add(vin)) issues.Add(new ChassisIssueDto { Index = i, Vin = vin, Code = "duplicate", Message = Messages.ChassisDuplicatedInDocument });
            else candidates.Add(i);
        }
        if (candidates.Count == 0) return issues;

        var lookup = candidates.Select(i => vins[i]).ToList();
        var existing = await db.Set<Vehicle>().AsNoTracking()
            .Where(v => lookup.Contains(v.ChassisNumber)
                && !(draftInvoiceId != null && v.Status == VehicleStatus.Draft && v.PurchaseInvoiceId == draftInvoiceId))
            .Select(v => new { v.ChassisNumber, v.Status, v.PurchaseInvoiceId }).ToListAsync(ct);
        if (existing.Count == 0) return issues;

        // شاسيه تحجزه مسودة فاتورة أخرى: تُذكر المسودة ليعرف المستخدم أين هو
        var draftInvoiceIds = existing.Where(e => e.Status == VehicleStatus.Draft && e.PurchaseInvoiceId.HasValue).Select(e => e.PurchaseInvoiceId!.Value).Distinct().ToList();
        var draftNumbers = draftInvoiceIds.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Set<Invoice>().AsNoTracking().Where(i => draftInvoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, ct);
        var byVin = existing.ToDictionary(e => e.ChassisNumber);
        foreach (var i in candidates)
        {
            if (!byVin.TryGetValue(vins[i], out var e)) continue;
            var message = e.Status == VehicleStatus.Draft && e.PurchaseInvoiceId.HasValue && draftNumbers.TryGetValue(e.PurchaseInvoiceId.Value, out var number)
                ? string.Format(Messages.ChassisExistsInDraft, number) : Messages.ChassisExists;
            issues.Add(new ChassisIssueDto { Index = i, Vin = vins[i], Code = "exists", Message = message });
        }
        return issues.OrderBy(x => x.Index).ToList();
    }
}

/// <summary>
/// دفعات السيارات في فاتورة الشراء: سطر لكل دفعة وصف InvoiceVehicleLine لكل شاسيه. كل شيء جماعي (استعلام واحد للتكرار، وحفظ واحد
/// للبطاقات) لتحتمل الدفعة مئات السيارات. بطاقة السيارة تُنشأ مع حفظ الفاتورة بحالة «مسودة» وتُدار من فاتورتها حتى الترحيل.
/// </summary>
internal static class VehicleBatchLines
{
    /// <summary>وصف مواصفات الدفعة: اسم بندها في الفاتورة.</summary>
    internal static string Spec(string brand, string model, string? trim, int year)
        => $"{brand} {model} {trim} {year}".Replace("  ", " ").Trim();

    internal static string Spec(InvoiceVehicleLine l) => Spec(l.BrandNameAr, l.ModelNameAr, l.TrimNameAr, l.Year);

    /// <summary>
    /// يتحقق من الدفعات وشواصيها، يولّد الأرقام المؤقتة المطلوبة، يحسب الأرقام، ويستبدل r.Items ببنود مشتقة:
    /// بند لكل دفعة بسعرها، وبند لكل سعر خاص مختلف فيها (فيبقى الإجمالي = مجموع أسعار الشواصي بلا كسور تقريب).
    /// </summary>
    public static async Task<(List<InvoiceVehicleBatch> Batches, List<InvoiceVehicleLine> Lines)> ResolveAsync(
        ErpDbContext db, IVehicleService vehicles, CreateInvoiceDto r, decimal invoiceDiscount, CancellationToken ct)
    {
        if (r.Kind != InvoiceKind.Purchase) throw new ValidationFailedException(Messages.BatchesOnlyForPurchase);
        if (r.VehicleLines.Count > 0) throw new ValidationFailedException(Messages.DoNotMixBatchesAndVehicleLines);
        if (r.Items.Count > 0) throw new ValidationFailedException(Messages.DoNotMixItemAndVehicleLines);

        var tax = await CarTax.LoadAsync(db, ct); // نسبة الفئة «خاضع» من إعدادات المعرض (يحددها المحاسب)
        var errors = new List<string>();
        for (var b = 0; b < r.VehicleBatches.Count; b++)
        {
            var d = r.VehicleBatches[b];
            var n = b + 1;
            if (string.IsNullOrWhiteSpace(d.BrandNameAr) || string.IsNullOrWhiteSpace(d.ModelNameAr)) errors.Add(string.Format(Messages.LineBrandAndModelRequired, n));
            if (d.Year is < 1980 or > 2100) errors.Add(string.Format(Messages.LineYearInvalid, n));
            if (d.VatMode is not (VatMode.Standard_15 or VatMode.Exempt)) errors.Add(string.Format(Messages.LinePurchaseVatModeInvalid, n));
            if (d.UnitPrice < 0 || d.Discount < 0 || d.Discount > d.UnitPrice) errors.Add(string.Format(Messages.LinePriceOrDiscountInvalid, n));
            if (d.GenerateCount < 0 || d.GenerateCount > VehicleService.MaxGeneratedVins) errors.Add(string.Format(Messages.VinGenerateCountInvalid, VehicleService.MaxGeneratedVins));
            if (d.Chassis.Count + Math.Max(0, d.GenerateCount) == 0) errors.Add(string.Format(Messages.BatchNeedsChassis, n));
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        // الأرقام المؤقتة تُولَّد هنا وتُثبَّت في الطلب، فإعادة التنفيذ بعد تعارض تزامن لا تولّد غيرها
        foreach (var d in r.VehicleBatches.Where(d => d.GenerateCount > 0))
        {
            foreach (var vin in await vehicles.GenerateProvisionalVinsAsync(d.GenerateCount, ct)) d.Chassis.Add(new InvoiceVehicleLineDto { Vin = vin });
            d.GenerateCount = 0;
        }

        // تحقق واحد لكل شواصي الفاتورة: التكرار داخل الدفعة وبين الدفعات ومع سيارات المنشأة
        var flat = r.VehicleBatches.SelectMany((d, b) => d.Chassis.Select(c => (Batch: b, Chassis: c, Vin: ChassisCheck.Normalize(c.Vin)))).ToList();
        var issues = await ChassisCheck.RunAsync(db, flat.Select(x => x.Vin).ToList(), r.ExistingInvoiceId, ct);
        if (issues.Count > 0)
        {
            var messages = issues.Select(i => string.Format(Messages.BatchChassisIssue, flat[i.Index].Batch + 1, i.Vin.Length == 0 ? "—" : i.Vin, i.Message)).ToList();
            if (issues.All(i => i.Code == "exists")) throw new ConflictException(string.Join(" ", messages.Take(5)));
            throw new ValidationFailedException(messages[0], messages);
        }

        // خصم الفاتورة على الشواصي بنسبة صافي كل منها، فيدخل في ثمن شراء كل سيارة
        var shares = VehicleInvoiceLines.SpreadInvoiceDiscount(invoiceDiscount,
            r.VehicleBatches.SelectMany(d => d.Chassis.Select(c => DocumentPricing.Round((c.PriceOverride ?? d.UnitPrice) - d.Discount))).ToList());

        var batches = new List<InvoiceVehicleBatch>();
        var lines = new List<InvoiceVehicleLine>();
        var items = new List<InvoiceItemDto>();
        var lineNo = 0;
        for (var b = 0; b < r.VehicleBatches.Count; b++)
        {
            var d = r.VehicleBatches[b];
            var rate = tax.RateFor(d.VatMode);
            var batch = new InvoiceVehicleBatch
            {
                Id = Guid.NewGuid(), LineNo = b + 1, BrandId = d.BrandId, ModelId = d.ModelId, TrimId = d.TrimId,
                BrandNameAr = d.BrandNameAr.Trim(), ModelNameAr = d.ModelNameAr.Trim(), TrimNameAr = string.IsNullOrWhiteSpace(d.TrimNameAr) ? null : d.TrimNameAr.Trim(),
                Year = d.Year, ColorExterior = (d.ColorExterior ?? string.Empty).Trim(), ColorInterior = (d.ColorInterior ?? string.Empty).Trim(),
                VatMode = d.VatMode, UnitPrice = d.UnitPrice, Discount = d.Discount, Quantity = d.Chassis.Count,
            };

            foreach (var c in d.Chassis)
            {
                var vin = ChassisCheck.Normalize(c.Vin);
                var price = c.PriceOverride ?? d.UnitPrice;
                if (price < d.Discount) throw new ValidationFailedException(string.Format(Messages.BatchChassisPriceInvalid, b + 1, vin));
                var share = shares[lineNo];
                var net = DocumentPricing.Round(price - d.Discount) - share;
                var vat = DocumentPricing.Round(net * rate / 100m);
                lines.Add(new InvoiceVehicleLine
                {
                    LineNo = ++lineNo, BatchId = batch.Id, Vin = vin, IsTemporaryChassis = ChassisCheck.IsProvisional(vin),
                    BrandId = batch.BrandId, ModelId = batch.ModelId, TrimId = batch.TrimId,
                    BrandNameAr = batch.BrandNameAr, ModelNameAr = batch.ModelNameAr, TrimNameAr = batch.TrimNameAr, Year = batch.Year,
                    ColorExterior = string.IsNullOrWhiteSpace(c.ColorExterior) ? batch.ColorExterior : c.ColorExterior.Trim(),
                    ColorInterior = string.IsNullOrWhiteSpace(c.ColorInterior) ? batch.ColorInterior : c.ColorInterior.Trim(),
                    EngineNumber = string.IsNullOrWhiteSpace(c.EngineNumber) ? null : c.EngineNumber.Trim(),
                    CustomsCardNumber = string.IsNullOrWhiteSpace(c.CustomsCardNumber) ? null : c.CustomsCardNumber.Trim(),
                    Notes = string.IsNullOrWhiteSpace(c.Notes) ? null : c.Notes.Trim(),
                    VatMode = d.VatMode, UnitPrice = price, PriceOverride = c.PriceOverride.HasValue && c.PriceOverride != d.UnitPrice ? c.PriceOverride : null,
                    Discount = d.Discount, InvoiceDiscountShare = share, TotalBeforeVat = net, VatAmount = vat, TotalAfterVat = net + vat,
                });
            }

            // بنود الفاتورة: كمية × سعر لكل سعر في الدفعة؛ ضريبة السطر على صافيه بنسبة فئته
            var name = Spec(batch.BrandNameAr, batch.ModelNameAr, batch.TrimNameAr, batch.Year);
            foreach (var group in lines.Where(l => l.BatchId == batch.Id).GroupBy(l => l.UnitPrice).OrderByDescending(g => g.Key == d.UnitPrice).ThenBy(g => g.Key))
            {
                var count = group.Count();
                // خصم البند يحمل حصص شواصيه من خصم الفاتورة، فلا يوزّعه محرك التسعير مرة ثانية
                var discount = count * d.Discount + group.Sum(l => l.InvoiceDiscountShare);
                var net = DocumentPricing.Round(count * group.Key - discount);
                batch.TotalBeforeVat += net;
                batch.VatAmount += DocumentPricing.Round(net * rate / 100m);
                items.Add(new InvoiceItemDto
                {
                    ItemId = Guid.Empty, ItemName = name, Unit = "سيارة", Quantity = count, UnitPrice = group.Key, Discount = discount, VatRate = rate,
                });
            }
            batch.TotalAfterVat = batch.TotalBeforeVat + batch.VatAmount;
            batches.Add(batch);
        }
        r.Items = items;
        return (batches, lines);
    }

    /// <summary>
    /// يطابق بطاقات السيارات المسودة مع شواصي الفاتورة (بلا حفظ؛ يحفظها المتصل مع الفاتورة): الشاسيه الباقي تُحدَّث بطاقته،
    /// الجديد تُنشأ له بطاقة، والمحذوف تُحذف بطاقته. فاتورة بلا دفعات تُحذف بطاقاتها المسودة (حذف المسودة أو تحويلها لغير الدفعات).
    /// </summary>
    public static async Task SyncDraftVehiclesAsync(ErpDbContext db, Invoice invoice, bool removeAll, CancellationToken ct)
    {
        var lines = removeAll ? new List<InvoiceVehicleLine>() : invoice.VehicleLines.Where(l => l.BatchId.HasValue).ToList();
        var existing = await db.Set<Vehicle>().Where(v => v.PurchaseInvoiceId == invoice.Id && v.Status == VehicleStatus.Draft).ToDictionaryAsync(v => v.ChassisNumber, ct);
        if (lines.Count == 0 && existing.Count == 0) return;

        var tax = await CarTax.LoadAsync(db, ct);
        var fx = invoice.ExchangeRate <= 0 ? 1 : invoice.ExchangeRate;
        foreach (var line in lines)
        {
            var isNew = !existing.Remove(line.Vin!, out var vehicle);
            if (isNew)
            {
                vehicle = new Vehicle
                {
                    Id = Guid.NewGuid(), Status = VehicleStatus.Draft, PurchaseInvoiceId = invoice.Id, Condition = VehicleCondition.New,
                    VatMode = VatMode.Standard_15, Location = string.Empty,
                };
                db.Add(vehicle);
            }
            vehicle!.ChassisNumber = line.Vin!; vehicle.IsTemporaryChassis = line.IsTemporaryChassis;
            vehicle.EngineNumber = line.EngineNumber; vehicle.CustomsCardNumber = line.CustomsCardNumber; vehicle.Notes = line.Notes;
            vehicle.BrandId = line.BrandId; vehicle.ModelId = line.ModelId; vehicle.TrimId = line.TrimId;
            vehicle.BrandNameAr = line.BrandNameAr; vehicle.ModelNameAr = line.ModelNameAr; vehicle.TrimNameAr = line.TrimNameAr; vehicle.Trim = line.TrimNameAr;
            vehicle.Year = line.Year; vehicle.ColorExterior = line.ColorExterior; vehicle.ColorInterior = line.ColorInterior;
            // ثمن الشراء بالعملة الأساسية؛ سعر البيع يُحدَّد لاحقاً من بطاقة المركبة
            vehicle.PurchasePrice = DocumentPricing.Round(line.TotalBeforeVat * fx);
            vehicle.SellingPrice = vehicle.PurchasePrice ?? 0;
            VehicleService.Recalculate(vehicle, tax);
            line.VehicleId = vehicle.Id;
        }

        if (existing.Count == 0) return;
        // شاسيه حُذف من المسودة: بطاقته تُحذف، إلا إن سُجّل عليها مصروف
        var removedIds = existing.Values.Select(v => v.Id).ToList();
        var charged = await db.Set<AdditionalChargeAllocation>().AsNoTracking().Where(a => removedIds.Contains(a.VehicleId)).Select(a => a.VehicleId).FirstOrDefaultAsync(ct);
        if (charged != Guid.Empty)
            throw new ConflictException(string.Format(Messages.DraftVehicleHasCharges, existing.Values.First(v => v.Id == charged).ChassisNumber));
        db.RemoveRange(existing.Values);
    }

    /// <summary>عند الترحيل: بطاقات الدفعات تخرج من المسودة إلى المخزون، واستعلامات تم السابقة على شواصيها تُنقل إلى تكلفتها.</summary>
    public static async Task OnPostingAsync(ErpDbContext db, IAccountingPostingService posting, Invoice invoice, CancellationToken ct)
    {
        var ids = invoice.VehicleLines.Where(l => l.BatchId.HasValue && l.VehicleId.HasValue).Select(l => l.VehicleId!.Value).ToList();
        if (ids.Count == 0) return;
        var vehicles = await db.Set<Vehicle>().Where(v => ids.Contains(v.Id)).ToListAsync(ct);
        var notDraft = vehicles.FirstOrDefault(v => v.Status != VehicleStatus.Draft);
        if (notDraft != null || vehicles.Count != ids.Count)
            throw new ConflictException(string.Format(Messages.BatchVehicleNotDraft, notDraft?.ChassisNumber ?? "—"));
        var now = DateTime.UtcNow;
        foreach (var v in vehicles)
        {
            v.Status = VehicleStatus.Available;
            v.ReceivedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await TammCharges.AttachPurchasedAsync(db, posting, vehicles.Where(v => !v.IsTemporaryChassis).Select(v => v.Id).ToList(), ct);
    }

    /// <summary>
    /// يملأ دفعات الفاتورة المقروءة بشواصيها وبما أُرجع منها. فاتورة شراء قديمة (سطر لكل سيارة بلا دفعات) تُعرض دفعاتٍ
    /// مجمَّعةً بالمواصفات والسعر لتراها الواجهة بالشكل نفسه؛ حفظها من جديد يحوّلها إلى دفعات فعلية.
    /// </summary>
    public static async Task FillAsync(ErpDbContext db, Invoice invoice, InvoiceDto dto, CancellationToken ct)
    {
        if (invoice.Kind != InvoiceKind.Purchase || dto.VehicleLines.Count == 0) return;

        var returned = new HashSet<Guid>();
        if (invoice.Status == "posted")
            returned = (await db.Set<InvoiceVehicleLine>().AsNoTracking()
                .Where(l => l.VehicleId != null && db.Set<Invoice>().Any(i => i.Id == l.InvoiceId && i.OriginalInvoiceId == invoice.Id && i.Status == "posted"))
                .Select(l => l.VehicleId!.Value).ToListAsync(ct)).ToHashSet();
        foreach (var line in dto.VehicleLines) line.IsReturned = line.VehicleId.HasValue && returned.Contains(line.VehicleId.Value);

        dto.VehicleBatches = dto.VehicleBatches.OrderBy(b => b.LineNo).ToList();
        foreach (var legacy in dto.VehicleLines.Where(l => l.BatchId == null)
                     .GroupBy(l => (l.BrandId, l.ModelId, l.TrimId, l.BrandNameAr, l.ModelNameAr, l.TrimNameAr, l.Year, l.VatMode, l.UnitPrice, l.Discount)))
        {
            var first = legacy.First();
            dto.VehicleBatches.Add(new InvoiceVehicleBatchDto
            {
                LineNo = dto.VehicleBatches.Count + 1, BrandId = first.BrandId, ModelId = first.ModelId, TrimId = first.TrimId,
                BrandNameAr = first.BrandNameAr, ModelNameAr = first.ModelNameAr, TrimNameAr = first.TrimNameAr, Year = first.Year,
                ColorExterior = first.ColorExterior, ColorInterior = first.ColorInterior, VatMode = first.VatMode,
                UnitPrice = first.UnitPrice, Discount = first.Discount, Quantity = legacy.Count(),
                TotalBeforeVat = legacy.Sum(l => l.TotalBeforeVat), VatAmount = legacy.Sum(l => l.VatAmount), TotalAfterVat = legacy.Sum(l => l.TotalAfterVat),
                Chassis = legacy.OrderBy(l => l.LineNo).ToList(),
            });
        }
        foreach (var batch in dto.VehicleBatches)
        {
            if (batch.Id != Guid.Empty) batch.Chassis = dto.VehicleLines.Where(l => l.BatchId == batch.Id).OrderBy(l => l.LineNo).ToList();
            batch.ReturnedQuantity = batch.Chassis.Count(c => c.IsReturned);
            batch.ReturnedTotal = batch.Chassis.Where(c => c.IsReturned).Sum(c => c.TotalBeforeVat);
        }
    }
}
