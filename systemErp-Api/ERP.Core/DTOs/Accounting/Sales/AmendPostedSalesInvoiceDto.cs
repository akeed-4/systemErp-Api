namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// التعديل المسموح لفاتورة بيع مرحّلة (ولو أُرسلت للهيئة): البائع، ومراكز التكلفة (العام في الترويسة والجدول على الأسطر)،
/// وموقع الصرف وفق الضوابط المخزنية. لا يمس أرقام الفاتورة ولا ضريبتها ولا ما أُرسل للهيئة. مستندات الأرشفة عبر المرفقات المشتركة.
/// </summary>
public class AmendPostedSalesInvoiceDto
{
    /// <summary>البائع (موظف نشط)؛ الفارغ = بلا بائع.</summary>
    public Guid? SellerEmployeeId { get; set; }
    /// <summary>مركز التكلفة العام (الترويسة).</summary>
    public Guid? CostCenterId { get; set; }
    /// <summary>موقع الفاتورة؛ الفارغ = يبقى كما هو.</summary>
    public Guid? WarehouseId { get; set; }
    /// <summary>الأسطر المراد تغيير مركزها أو موقعها؛ السطر غير المذكور يبقى كما هو.</summary>
    public List<AmendPostedSalesLineDto> Lines { get; set; } = new();
    /// <summary>سبب التعديل (إلزامي، يُسجَّل في التدقيق).</summary>
    public string? Reason { get; set; }
}

public class AmendPostedSalesLineDto
{
    public Guid LineId { get; set; }
    /// <summary>مركز تكلفة السطر (الجدول)؛ الفارغ = يتبع مركز الترويسة في القيد.</summary>
    public Guid? CostCenterId { get; set; }
    /// <summary>موقع صرف السطر؛ الفارغ = يبقى كما هو. السطر المخزني صُرف عند الترحيل فلا يتغير موقعه (تحويل مخزني).</summary>
    public Guid? WarehouseId { get; set; }
}

/// <summary>خيار بائع لفاتورة البيع: موظف نشط برمزه واسمه فقط.</summary>
public class InvoiceSellerOptionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
}
