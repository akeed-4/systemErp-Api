using System.Reflection;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// نوع مستند يخضع لنطاق الوصول وحد الاعتماد (ومراحل العقد إن وُجدت). المفتاح يُستخدم في سمة <c>ScopedRecord</c>
/// على الـ controller وفي شاشة فحص الصلاحية. الخصائص بأسمائها لتُقرأ من أي كيان دون تكرار منطق لكل نوع.
/// </summary>
public sealed record ScopedRecordType(
    string Key, Type EntityType, string ScreenId, string NameAr, string NameEn,
    string NumberProperty, string? AmountProperty, string? StageProperty = null)
{
    /// <summary>مراحله معرّفة في ContractStageDefinition (الإجراءات المسموحة وإجراءات الانتقال لكل مرحلة).</summary>
    public bool HasStages => StageProperty != null;
    public PropertyInfo? WarehouseProperty { get; } = EntityType.GetProperty("WarehouseId");
}

/// <summary>حقائق سجل محدد لازمة لفحص النطاق والمرحلة والحد.</summary>
public sealed class RecordFacts
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string? Stage { get; init; }
    public Guid? WarehouseId { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public Guid? AssignedToUserId { get; init; }
}

public static class ScopedRecordTypes
{
    public const string CommercialContractKey = "commercial-contract";

    public static readonly ScopedRecordType[] All =
    {
        new(CommercialContractKey, typeof(CommercialContract), PermissionCatalog.CommercialContractsScreenId, "عقد تجاري", "Commercial Contract",
            nameof(CommercialContract.ContractNumber), nameof(CommercialContract.TotalValueWithVat),
            nameof(CommercialContract.Stage)),
        new("sales-invoice", typeof(SalesInvoice), "sales", "فاتورة مبيعات", "Sales Invoice", nameof(InvoiceBase.InvoiceNumber), nameof(InvoiceBase.GrandTotal)),
        new("sales-return", typeof(SalesReturn), "sales-returns", "مرتجع مبيعات", "Sales Return", nameof(InvoiceBase.InvoiceNumber), nameof(InvoiceBase.GrandTotal)),
        new("purchase-invoice", typeof(PurchaseInvoice), "purchases", "فاتورة مشتريات", "Purchase Invoice", nameof(InvoiceBase.InvoiceNumber), nameof(InvoiceBase.GrandTotal)),
        new("purchase-return", typeof(PurchaseReturn), "purchases", "مرتجع مشتريات", "Purchase Return", nameof(InvoiceBase.InvoiceNumber), nameof(InvoiceBase.GrandTotal)),
        new("quotation", typeof(Quotation), "sales", "عرض سعر", "Quotation", nameof(Quotation.QuotationNumber), nameof(Quotation.GrandTotal)),
        new("commercial-order", typeof(CommercialOrder), "sales", "أمر بيع/شراء", "Commercial Order", nameof(CommercialOrder.OrderNumber), nameof(CommercialOrder.GrandTotal)),
        new("delivery-note", typeof(DeliveryNote), "sales", "بيان تسليم", "Delivery Note", nameof(DeliveryNote.DeliveryNumber), nameof(DeliveryNote.GrandTotal)),
        new("material-requisition", typeof(MaterialRequisition), "purchases", "طلب مواد", "Material Requisition", nameof(MaterialRequisition.RequisitionNumber), nameof(MaterialRequisition.TotalEstimatedCost)),
        new("voucher", typeof(Voucher), "vouchers", "سند", "Voucher", nameof(Voucher.VoucherNumber), nameof(Voucher.Amount)),
        new("journal-entry", typeof(JournalEntry), "accounts", "قيد يومية", "Journal Entry", nameof(JournalEntry.EntryNumber), nameof(JournalEntry.TotalDebit)),
        new("car-sales-contract", typeof(CarSalesContract), "car-showroom", "عقد بيع سيارة", "Car Sales Contract", nameof(CarSalesContract.ContractNumber), nameof(CarSalesContract.TotalWithVat)),
        new("car-procurement-order", typeof(CarProcurementOrder), "car-showroom", "طلب شراء سيارات", "Car Procurement Order", nameof(CarProcurementOrder.OrderNumber), nameof(CarProcurementOrder.GrandTotal)),
    };

    private static readonly Dictionary<string, ScopedRecordType> ByKey = All.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
    public static ScopedRecordType? Find(string? key) => key == null ? null : ByKey.GetValueOrDefault(key);

    private static readonly MethodInfo LoadMethod = typeof(ScopedRecordTypes).GetMethod(nameof(LoadCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo SearchMethod = typeof(ScopedRecordTypes).GetMethod(nameof(SearchCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>حقائق السجل (null إن لم يوجد في المنشأة).</summary>
    public static Task<RecordFacts?> LoadAsync(ErpDbContext db, ScopedRecordType type, Guid id, CancellationToken ct)
        => (Task<RecordFacts?>)LoadMethod.MakeGenericMethod(type.EntityType).Invoke(null, new object[] { db, type, id, ct })!;

    public static Task<List<RecordFacts>> SearchAsync(ErpDbContext db, ScopedRecordType type, string? term, CancellationToken ct)
        => (Task<List<RecordFacts>>)SearchMethod.MakeGenericMethod(type.EntityType).Invoke(null, new object[] { db, type, term ?? string.Empty, ct })!;

    private static async Task<RecordFacts?> LoadCoreAsync<T>(ErpDbContext db, ScopedRecordType type, Guid id, CancellationToken ct) where T : BaseEntity
    {
        var entity = await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity == null ? null : Facts(type, entity);
    }

    private static async Task<List<RecordFacts>> SearchCoreAsync<T>(ErpDbContext db, ScopedRecordType type, string term, CancellationToken ct) where T : BaseEntity
    {
        var query = db.Set<T>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(term)) query = query.Where(e => EF.Property<string>(e, type.NumberProperty).Contains(term.Trim()));
        var rows = await query.OrderByDescending(e => e.CreatedAt).Take(20).ToListAsync(ct);
        return rows.Select(r => Facts(type, r)).ToList();
    }

    private static RecordFacts Facts(ScopedRecordType type, object entity)
    {
        var t = entity.GetType();
        object? Get(string? name) => name == null ? null : t.GetProperty(name)?.GetValue(entity);
        var scoped = entity as IScopedRecord;
        return new RecordFacts
        {
            Id = ((BaseEntity)entity).Id,
            Number = Get(type.NumberProperty)?.ToString() ?? string.Empty,
            Amount = Get(type.AmountProperty) is decimal d ? d : null,
            Stage = Get(type.StageProperty)?.ToString(),
            WarehouseId = type.WarehouseProperty?.GetValue(entity) as Guid?,
            CreatedByUserId = scoped?.CreatedByUserId,
            AssignedToUserId = scoped?.AssignedToUserId,
        };
    }
}
