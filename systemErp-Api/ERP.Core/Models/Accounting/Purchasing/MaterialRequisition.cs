
namespace ERP.Core.Models.Accounting;

public class MaterialRequisition : BaseEntity, IScopedRecord
{
    /// <summary>منشئ المستند (يُختم آلياً) لنطاق «السجلات التي أنشأها».</summary>
    public Guid? CreatedByUserId { get; set; }
    /// <summary>المستخدم المسند إليه المستند لنطاق «السجلات المسندة إليه».</summary>
    public Guid? AssignedToUserId { get; set; }
    public string RequisitionNumber { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public DateTime RequiredDate { get; set; }
    public string Department { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium"; // low, medium, high, urgent
    
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    
    /// <summary>المشروع (عقد مقاولات) الذي يُطلب له: كمياته تُقارن باحتياجات المشروع المعتمدة وما طُلب سابقاً.</summary>
    public Guid? ContractId { get; set; }
    /// <summary>
    /// مرحلة التنفيذ التي يُطلب لها (<see cref="ProjectPhase"/> من المشروع نفسه؛ بلا قيد كبقية مراجع المراحل، وحذف المرحلة يُمنع ما دام لها
    /// طلبات). إلزامية لطلب جديد في مشروع موادُّه المعتمدة مسندة لمراحل، وتثبت بعد الإنشاء فلا يغيّر تقدم المراحل سجلات المراحل السابقة.
    /// فارغة في الطلبات القديمة وفي مشروع بلا مراحل (تُقاس على المشروع كله).
    /// </summary>
    public Guid? PhaseId { get; set; }
    /// <summary>موقع التوريد (<see cref="RequisitionDeliveryLocations"/>): المستودع، أو موقع المشروع مباشرة.</summary>
    public string DeliveryLocation { get; set; } = RequisitionDeliveryLocations.Warehouse;
    /// <summary>عنوان موقع التوريد (موقع المشروع افتراضياً).</summary>
    public string? DeliveryAddress { get; set; }

    public decimal TotalEstimatedCost { get; set; }
    public string Status { get; set; } = "draft";
    
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovalDate { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }

    public virtual ICollection<MaterialRequisitionItem> Items { get; set; } = new List<MaterialRequisitionItem>();
}
