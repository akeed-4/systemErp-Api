using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// امتداد وحدة (مثل المعارض) على دورة الفاتورة: InvoiceService يستدعي هذه النقاط دون أن يعرف منطق الوحدة،
/// وكل نقطة لا تخص الوحدة تعود دون أثر. تُسجَّل كل الامتدادات وتُستدعى بالترتيب.
/// </summary>
public interface IInvoiceExtension
{
    /// <summary>الوحدة التي تُنسب إليها الفاتورة حين يخصها الامتداد (Invoice.Module).</summary>
    string Module { get; }

    /// <summary>
    /// قبل التحقق: يشتق r.Items من أسطر الوحدة ويُرجع خطوة تُلحق الأسطر بالفاتورة بعد بنائها (وتضبط حساباتها الافتراضية).
    /// null = الطلب لا يخص الوحدة. لا أثر جانبي على قاعدة البيانات.
    /// </summary>
    Task<Func<Invoice, CancellationToken, Task>?> ResolveAsync(CreateInvoiceDto r, CancellationToken ct);

    /// <summary>المستند المرجعي من مستندات الوحدة (عقد بيع، أمر شراء...).</summary>
    Task<bool> OwnsReferenceAsync(string? referenceType, Guid referenceId, CancellationToken ct);

    /// <summary>قبل حفظ إنشاء الفاتورة أو تعديلها أو حذفها (<paramref name="deleting"/>): يزامن سجلات الوحدة التابعة لها.</summary>
    Task OnSavingAsync(Invoice invoice, bool deleting, CancellationToken ct);

    /// <summary>عند الترحيل، قبل بناء القيد.</summary>
    Task OnPostingAsync(Invoice invoice, CancellationToken ct);

    /// <summary>عند إلغاء ترحيل فاتورة لتعديلها أو حذفها: يعيد سجلات الوحدة لحالتها قبل الترحيل أو يمنع ذلك.</summary>
    Task OnUnpostingAsync(Invoice invoice, CancellationToken ct);

    /// <summary>
    /// مرتجع فاتورة تخص الوحدة، يُنشأ عبر <paramref name="create"/> (إنشاء الفاتورة المركزي). null = الأصل لا يخص الوحدة.
    /// </summary>
    Task<InvoiceDto?> CreateReturnAsync(CreateReturnInvoiceRequestDto r, Invoice original,
        Func<CreateInvoiceDto, CancellationToken, Task<InvoiceDto>> create, CancellationToken ct);

    /// <summary>يُكمل بيانات الوحدة في عرض الفاتورة.</summary>
    Task FillAsync(Invoice invoice, InvoiceDto dto, CancellationToken ct);

    /// <summary>يحجب من نتائج القراءة ما لا يملك المستخدم صلاحية رؤيته.</summary>
    Task MaskAsync(IReadOnlyList<(Invoice Invoice, InvoiceDto Dto)> rows, CancellationToken ct);

    /// <summary>يمنع عرض قيد الفاتورة (أو معاينته) إن كان يُظهر ما تحجبه الوحدة عن المستخدم.</summary>
    Task EnsureJournalVisibleAsync(Invoice invoice, CancellationToken ct);
}
