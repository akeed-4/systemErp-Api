namespace ERP.Core.DTOs.Accounting;

/// <summary>فحص الجودة لقطع في مرحلة الجودة: الاعتماد يجعلها «جاهزة»، والرفض يفتح طلب تعديل بالملاحظة.</summary>
public class TailoringInspectDto
{
    public List<Guid> GarmentIds { get; set; } = new();
    public bool Approved { get; set; }
    /// <summary>ملاحظة الاعتماد، أو تعليمات التعديل عند الرفض (مطلوبة).</summary>
    public string? Note { get; set; }
    public Guid? EmployeeId { get; set; }
}

public class TailoringDeliveryPreviewRequestDto
{
    public Guid OrderId { get; set; }
    public List<Guid> GarmentIds { get; set; } = new();
}

public class DeliverTailoringDto
{
    public Guid OrderId { get; set; }
    public List<Guid> GarmentIds { get; set; } = new();
    public DateTime? Date { get; set; }
    public string? ReceivedBy { get; set; }
    public string? ReceiverPhone { get; set; }
    /// <summary>توقيع المستلم صورة PNG (data URL).</summary>
    public string? SignatureImage { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? PaymentMethodCode { get; set; }
    public string? Notes { get; set; }
}

/// <summary>مبالغ التسليم كما يحسبها الخادم (المعاينة والتسليم نفسه).</summary>
public class TailoringDeliveryAmountsDto
{
    public int PieceCount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal AdvanceRecovery { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsFinal { get; set; }
    /// <summary>الدفعة تغطي التسليم كله: تسوية بلا فاتورة ضريبية.</summary>
    public bool SettlementOnly { get; set; }
}

public class TailoringDeliveryPieceDto
{
    public Guid GarmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string GarmentTypeName { get; set; } = string.Empty;
    public string? WearerName { get; set; }
    public decimal NetAmount { get; set; }
    public decimal AdvanceRecovery { get; set; }
    /// <summary>بصلاحية رؤية التكلفة فقط.</summary>
    public decimal? CostAmount { get; set; }
}

public class TailoringDeliveryDto : TailoringDeliveryAmountsDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReceivedBy { get; set; }
    public string? ReceiverPhone { get; set; }
    public string? SignatureImage { get; set; }
    public string? Notes { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal? CostAmount { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<TailoringDeliveryPieceDto> Pieces { get; set; } = new();
}

/// <summary>شاشة تسليم أمر: القطع الجاهزة والمسلّمة وباقي الدفعة والتسليمات السابقة.</summary>
public class TailoringOrderDeliveryDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public decimal NetTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AdvanceAmount { get; set; }
    public decimal AdvanceNet { get; set; }
    public decimal AdvanceRecovered { get; set; }
    public decimal DeliveredTotal { get; set; }
    public int PieceCount { get; set; }
    public int DeliveredCount { get; set; }
    public bool CanClose { get; set; }
    public List<TailoringDeliveryPieceDto> ReadyPieces { get; set; } = new();
    /// <summary>القطع غير الجاهزة بعد (للعرض: رمزها وحالتها).</summary>
    public List<TailoringGarmentDto> PendingPieces { get; set; } = new();
    public List<TailoringDeliveryDto> Deliveries { get; set; } = new();
}
