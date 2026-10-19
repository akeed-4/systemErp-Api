using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Core.Models.Accounting;

/// <summary>
/// رأس فاتورة البيع/الشراء ومرتجعاتهما: الحقول المشتركة بين جداول المستندات المستقلة (TPC: جدول فعلي لكل نوع).
/// التجارة العامة: SalesInvoice، PurchaseInvoice، SalesReturn، PurchaseReturn. المعارض: Invoice (بكل أنواعها).
/// المنطق المشترك (البناء، الترحيل، الضريبة، الأرصدة) يعمل على هذا النوع، فيُستعلم عن الجداول كلها معاً بـ Set&lt;InvoiceBase&gt;.
/// </summary>
public abstract class InvoiceBase : BaseEntity
{
    public string Module { get; set; } = "accounting";
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid Uuid { get; set; } = Guid.NewGuid();
    public DateTime IssueDate { get; set; }
    public string IssueTime { get; set; } = string.Empty;
    public InvoiceKind Kind { get; set; } = InvoiceKind.Sales;
    public InvoiceType InvoiceType { get; set; }
    
    public string PartyName { get; set; } = string.Empty;
    public Guid? PartyId { get; set; }
    public string? PartyPhone { get; set; }
    public string? PartyEmail { get; set; }
    public string? PartyVatNumber { get; set; }
    public string? PartyCrNumber { get; set; }
    /// <summary>الهوية الأخرى للمشتري (نوعها ورقمها) عند غياب السجل التجاري.</summary>
    public string? PartyIdType { get; set; }
    public string? PartyIdNumber { get; set; }
    public string? PartyAddress { get; set; }
    /// <summary>المستودع الذي تدخل إليه/تخرج منه أصناف الفاتورة (الافتراضي عند عدم التحديد).</summary>
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    /// <summary>العنوان الوطني للطرف (إلزامي لمشتري الفاتورة الضريبية): الشارع، رقم المبنى، الحي، المدينة، الرمز البريدي، الدولة.</summary>
    public string? PartyStreet { get; set; }
    public string? PartyBuildingNo { get; set; }
    public string? PartyDistrict { get; set; }
    public string? PartyCity { get; set; }
    public string? PartyPostalCode { get; set; }
    public string? PartyAdditionalNo { get; set; }
    /// <summary>رمز الدولة (SA افتراضياً)؛ المشتري خارج المملكة لا يُشترط له رقم ضريبي سعودي.</summary>
    public string? PartyCountry { get; set; }
    
    public PaymentMethod PaymentMethod { get; set; }
    /// <summary>رمز طريقة الدفع المؤسَّسة في النظام (جدول طرق الدفع): يُرحَّل على حسابها المرتبط. فارغ = أول طريقة فعّالة من النوع.</summary>
    public string? PaymentMethodCode { get; set; }
    public bool IsSplitPayment { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1.0m;
    
    public decimal Subtotal { get; set; }
    public decimal ItemsDiscountTotal { get; set; }
    public decimal InvoiceDiscount { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    
    public decimal TotalCost { get; set; }
    public decimal GrossProfit { get; set; }
    
    public string Status { get; set; } = "draft"; // draft | posted | cancelled
    public ZatcaSubmissionStatus ZatcaStatus { get; set; } = ZatcaSubmissionStatus.NotSubmitted;
    
    public string? ZatcaHash { get; set; }
    public string? ZatcaQrCode { get; set; }
    public string? ZatcaUblXml { get; set; }
    public string? ZatcaPih { get; set; }
    public string? ZatcaValidationMessages { get; set; }
    /// <summary>خيار الشاشة «إرسال للهيئة»: تُرسل الفاتورة لهيئة الزكاة فور ترحيلها (حتى لو رُحّلت لاحقاً من مسودة).</summary>
    public bool SendToZatca { get; set; }
    
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }

    /// <summary>وثيقة المصدر التي أُنشئت الفاتورة منها تلقائياً (أمر توريد، عقد بيع سيارة، ...).</summary>
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }

    public bool IsReturn { get; set; }
    public Guid? OriginalInvoiceId { get; set; }
    public string? OriginalInvoiceNumber { get; set; }
    public string? ReturnReason { get; set; }

    // حسابات بديلة للترحيل (فارغة = الحسابات الافتراضية): مثال فاتورة سيارات تستخدم إيراد/تكلفة/مخزون السيارات.
    public string? RevenueAccountCode { get; set; }
    public string? InventoryAccountCode { get; set; }
    public string? CogsAccountCode { get; set; }
    /// <summary>حساب التحصيل للدفعات غير الآجلة بدل حساب طريقة الدفع (مثل تحصيلات بوابة الدفع 1113).</summary>
    public string? SettlementAccountCode { get; set; }

    public virtual ICollection<InvoicePaymentSplit> PaymentSplits { get; set; } = new List<InvoicePaymentSplit>();

    /// <summary>بنود المستند بنوعها الأساس (للقراءة في المنطق المشترك). تُضاف بـ <see cref="AddLine"/>.</summary>
    [NotMapped]
    public abstract IEnumerable<InvoiceItemBase> Lines { get; }
    /// <summary>بند جديد من نوع بنود هذا المستند (غير مضاف بعد).</summary>
    public abstract InvoiceItemBase NewLine();
    public abstract void AddLine(InvoiceItemBase line);
    public abstract void ClearLines();
}

/// <summary>رأس مستند ببنوده المستقلة من النوع <typeparamref name="TItem"/> (جدول تفاصيل لكل جدول رأس).</summary>
public abstract class InvoiceBase<TItem> : InvoiceBase where TItem : InvoiceItemBase, new()
{
    public virtual ICollection<TItem> Items { get; set; } = new List<TItem>();

    public override IEnumerable<InvoiceItemBase> Lines => Items;
    public override InvoiceItemBase NewLine() => new TItem();
    public override void AddLine(InvoiceItemBase line) => Items.Add((TItem)line);
    public override void ClearLines() => Items.Clear();
}
