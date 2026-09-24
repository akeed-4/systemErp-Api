using System.Text;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class ZatcaService : IZatcaService
{
    private readonly ErpDbContext _db;
    public ZatcaService(ErpDbContext db) => _db = db;

    public string GenerateTlvQr(string sellerName, string vatNumber, DateTime timestamp, decimal total, decimal vat)
        => ZatcaTlv.Encode(sellerName, vatNumber, timestamp, total, vat);

    /// <summary>
    /// الإرسال الفعلي لهيئة الزكاة (توقيع ECDSA وUBL 2.1 وCSID) يتطلب بيانات تسجيل حقيقية للمنشأة، وهي غير متوفرة
    /// في الكود. لذلك لا تُعاد حالة "cleared/reported" مصطنعة: تُعاد نتيجة صريحة بأن الإرسال غير مُفعَّل.
    /// </summary>
    public async Task<ZatcaSubmitResultDto> SubmitInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _db.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new NotFoundException("الفاتورة غير موجودة");
        if (invoice.Status != "posted") throw new ConflictException("تُرسل الفواتير المرحّلة فقط.");
        if (invoice.Kind is InvoiceKind.Purchase or InvoiceKind.PurchaseReturn) throw new ConflictException("فواتير المشتريات لا تُرسل لهيئة الزكاة.");

        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);
        if (!tenant.ZatcaConfig.IsEnabled)
            return new ZatcaSubmitResultDto { Success = false, Status = invoice.ZatcaStatus, Message = "الربط مع هيئة الزكاة غير مُفعَّل في إعدادات المنشأة.", QrCode = invoice.ZatcaQrCode };

        invoice.ZatcaValidationMessages = "الإرسال الفعلي لهيئة الزكاة غير مُنفَّذ في هذه النسخة (يتطلب شهادة CSID حقيقية).";
        await _db.SaveChangesAsync(ct);
        return new ZatcaSubmitResultDto { Success = false, Status = invoice.ZatcaStatus, Message = invoice.ZatcaValidationMessages, QrCode = invoice.ZatcaQrCode };
    }
}
