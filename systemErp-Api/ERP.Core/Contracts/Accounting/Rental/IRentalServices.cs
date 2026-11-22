using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>إعداد التأجير للعملاء: الفئات، والأسعار (فئة أو معدة)، والمعدات القابلة للتأجير من سجل المعدات القائم.</summary>
public interface IRentalSetupService
{
    Task<List<RentalCategoryDto>> CategoriesAsync(CancellationToken ct = default);
    Task<RentalCategoryDto> CreateCategoryAsync(SaveRentalCategoryDto request, CancellationToken ct = default);
    Task<RentalCategoryDto> UpdateCategoryAsync(Guid id, SaveRentalCategoryDto request, CancellationToken ct = default);
    Task DeleteCategoryAsync(Guid id, CancellationToken ct = default);

    Task<List<RentalRateDto>> RatesAsync(Guid? categoryId, Guid? equipmentId, CancellationToken ct = default);
    Task<RentalRateDto> CreateRateAsync(SaveRentalRateDto request, CancellationToken ct = default);
    Task<RentalRateDto> UpdateRateAsync(Guid id, SaveRentalRateDto request, CancellationToken ct = default);
    Task DeleteRateAsync(Guid id, CancellationToken ct = default);

    /// <summary>سجل المعدات بإعداد تأجيرها؛ <paramref name="rentableOnly"/> للقابلة للتأجير وحدها.</summary>
    Task<List<RentalEquipmentDto>> EquipmentAsync(bool rentableOnly, CancellationToken ct = default);
    Task<RentalEquipmentDto> UpdateEquipmentAsync(Guid equipmentId, UpdateRentalEquipmentDto request, CancellationToken ct = default);
}

/// <summary>
/// التوفر والحجوزات: تقويم الفترات المشغولة لكل معدة قابلة للتأجير (إسناد المقاولات، الحجوزات، الصيانة، الحالة، عقد المورد)،
/// والتقدير، والحجز (مسودة ← مؤقت حتى موعد ← مؤكد، أو إلغاء بسبب). الحجز المؤقت والمؤكد يمنعان تعارض الفترات بقفل صف المعدة.
/// </summary>
public interface IRentalBookingService
{
    Task<List<RentalCalendarRowDto>> CalendarAsync(DateTime from, DateTime to, Guid? categoryId, Guid? warehouseId, CancellationToken ct = default);
    Task<RentalQuoteDto> QuoteAsync(RentalQuoteRequestDto request, CancellationToken ct = default);

    Task<List<RentalBookingDto>> ListAsync(string? status, Guid? customerId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<RentalBookingDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<RentalBookingDto> CreateAsync(SaveRentalBookingDto request, CancellationToken ct = default);
    Task<RentalBookingDto> UpdateAsync(Guid id, SaveRentalBookingDto request, CancellationToken ct = default);
    Task<RentalBookingDto> HoldAsync(Guid id, HoldRentalBookingDto request, CancellationToken ct = default);
    Task<RentalBookingDto> ConfirmAsync(Guid id, CancellationToken ct = default);
    Task<RentalBookingDto> CancelAsync(Guid id, RentalReasonDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// عقود تأجير المعدات للعملاء: مسودة (تحجز فترات معداتها بعد فحص التوفر بقفل) ← ساري (اعتماد) ← مغلق، أو ملغى بسبب قبل أي تسليم.
/// التمديد يفحص الفترة الإضافية ويسجّل التغيير. التسليم والاستلام والفوترة في مراحلها.
/// </summary>
public interface IRentalContractService
{
    Task<List<RentalContractDto>> ListAsync(string? status, Guid? customerId, CancellationToken ct = default);
    Task<RentalContractDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<RentalContractDto> CreateAsync(SaveRentalContractDto request, CancellationToken ct = default);
    /// <summary>
    /// يحوّل حجزاً مؤكداً أو مؤقتاً سارياً (أسطره معدات محددة) إلى مسودة عقد بأسعار لقطة الحجز، ويعلّم الحجز «محوَّلاً» مرتبطاً بالعقد.
    /// </summary>
    Task<RentalContractDto> ConvertFromBookingAsync(Guid bookingId, string term, CancellationToken ct = default);
    Task<RentalContractDto> UpdateAsync(Guid id, SaveRentalContractDto request, CancellationToken ct = default);
    Task<RentalContractDto> ActivateAsync(Guid id, CancellationToken ct = default);
    Task<RentalContractDto> ExtendAsync(Guid id, ExtendRentalContractDto request, CancellationToken ct = default);
    /// <summary>ينشئ عقداً لاحقاً (مسودة) مرتبطاً بالساري من نهاية أسطره المختارة، ويسجّل التجديد على السابق؛ المعدة الخارجة تنتقل إليه بمحضر تسليمه.</summary>
    Task<RentalContractDto> RenewAsync(Guid id, RenewRentalContractDto request, CancellationToken ct = default);
    /// <summary>تعيين مشغّل سطر بمشغّل أو تغييره أو إزالته (مسودة أو ساري، السطر بانتظار التسليم أو خارج)، بفحص تعارض المشغّل ويسجّل التغيير.</summary>
    Task<RentalContractDto> AssignOperatorAsync(Guid id, Guid lineId, AssignRentalOperatorDto request, CancellationToken ct = default);
    Task<RentalContractDto> CancelAsync(Guid id, RentalReasonDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// محاضر تسليم المعدات للعميل واستلامها منه (عقد ساري): التسليم يُخرج الأسطر المعلّقة والاستلام يعيد الخارجة، بعدّادها ووقودها
/// وحالتها، ويحدّثان عدّاد المعدة وحالتها. الإلغاء بسبب يعكس الأثر ما دام المحضر آخر حركة على أسطره ولم تُفوتر.
/// </summary>
public interface IRentalHandoverService
{
    Task<List<RentalHandoverDto>> ListAsync(Guid? contractId, string? kind, CancellationToken ct = default);
    Task<RentalHandoverDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<RentalHandoverDto> CreateAsync(SaveRentalHandoverDto request, CancellationToken ct = default);
    Task<RentalHandoverDto> CancelAsync(Guid id, RentalReasonDto request, CancellationToken ct = default);
}

/// <summary>
/// مهام نقل معدات عقود التأجير (مسودة أو ساري): مجدولة ← منفَّذة أو ملغاة بسبب. سجل تشغيلي بتكلفته والمبلغ على العميل، بلا قيود.
/// </summary>
public interface IRentalTransportService
{
    Task<List<RentalTransportJobDto>> ListAsync(Guid? contractId, string? status, CancellationToken ct = default);
    Task<RentalTransportJobDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<RentalTransportJobDto> CreateAsync(SaveRentalTransportJobDto request, CancellationToken ct = default);
    Task<RentalTransportJobDto> UpdateAsync(Guid id, SaveRentalTransportJobDto request, CancellationToken ct = default);
    Task<RentalTransportJobDto> CompleteAsync(Guid id, CompleteRentalTransportJobDto request, CancellationToken ct = default);
    Task<RentalTransportJobDto> CancelAsync(Guid id, RentalReasonDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// الرسوم والفوترة (R3a): الإعداد وأنواع الرسوم، والرسوم اليدوية والمولّدة (الإضافي، التأخير، النقل)، والفوترة الدورية عبر محرك فواتير
/// المبيعات (مرجع <c>rental_billing</c>)، وإلغاء آخر فوترة، وإغلاق العقد بعد الاستلام والفوترة.
/// </summary>
public interface IRentalBillingService
{
    Task<RentalSettingsDto> SettingsAsync(CancellationToken ct = default);
    Task<RentalSettingsDto> SaveSettingsAsync(RentalSettingsDto request, CancellationToken ct = default);
    Task<List<RentalChargeTypeDto>> ChargeTypesAsync(CancellationToken ct = default);
    Task<RentalChargeTypeDto> SaveChargeTypeAsync(SaveRentalChargeTypeDto request, Guid? id, CancellationToken ct = default);

    Task<List<RentalChargeDto>> ChargesAsync(Guid? contractId, string? status, CancellationToken ct = default);
    Task<RentalChargeDto> AddChargeAsync(SaveRentalChargeDto request, CancellationToken ct = default);
    Task<RentalChargeDto> ApproveChargeAsync(Guid id, ApproveRentalChargeDto request, CancellationToken ct = default);
    Task<RentalChargeDto> WaiveChargeAsync(Guid id, RentalReasonDto request, CancellationToken ct = default);
    Task DeleteChargeAsync(Guid id, CancellationToken ct = default);
    /// <summary>يولّد رسوم الإضافي والتأخير للأسطر المستلمة، ورسوم النقل المنفَّذة على العميل (مرة واحدة لكل مصدر).</summary>
    Task<List<RentalChargeDto>> GenerateChargesAsync(Guid contractId, CancellationToken ct = default);

    Task<RentalBillingPreviewDto> PreviewAsync(Guid contractId, DateTime? until, CancellationToken ct = default);
    Task<RentalBillingDto> BillAsync(Guid contractId, BillRentalContractDto request, CancellationToken ct = default);
    Task<List<RentalBillingDto>> BillingsAsync(Guid? contractId, CancellationToken ct = default);
    Task<RentalBillingDto> CancelBillingAsync(Guid id, RentalReasonDto request, CancellationToken ct = default);
    Task<RentalContractDto> CloseContractAsync(Guid contractId, CancellationToken ct = default);
}

/// <summary>
/// تأمينات العقود (R3b) عبر خدمة السندات القائمة: القبض (سند قبض لحساب تأمينات العملاء)، والخصم من فاتورة العقد (سند قبض موزّع على
/// الفاتورة خزينته حساب التأمينات)، والرد (سند صرف من حساب التأمينات)، وإلغاء آخر أثر بحذف سنده وعكس قيده.
/// </summary>
public interface IRentalDepositService
{
    Task<List<RentalDepositDto>> ListAsync(Guid? contractId, string? status, CancellationToken ct = default);
    Task<RentalDepositDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<RentalDepositDto> ReceiveAsync(ReceiveRentalDepositDto request, CancellationToken ct = default);
    Task<RentalDepositDto> ApplyAsync(Guid id, ApplyRentalDepositDto request, CancellationToken ct = default);
    Task<RentalDepositDto> RefundAsync(Guid id, RefundRentalDepositDto request, CancellationToken ct = default);
    Task<RentalDepositDto> CancelSettlementAsync(Guid id, Guid settlementId, RentalReasonDto request, CancellationToken ct = default);
    Task<RentalDepositDto> CancelAsync(Guid id, RentalReasonDto request, CancellationToken ct = default);
}

/// <summary>
/// كشوف ساعات التأجير (T1): كشف لكل سطر وفترة بأيامه، يُقدَّم ويعتمده ممثل العميل (صورة الكشف الموقّع بالإعداد، والتوقيع اختياري)
/// فتُجمَّد مبالغه بشروط السطر، ويُفوتر منه عقد «فوترة بكشف الساعات». الرفض يعيده للتعديل، والمعتمد غير المفوتر يُعاد فتحه بسبب.
/// </summary>
public interface IRentalTimesheetService
{
    Task<List<RentalTimesheetDto>> ListAsync(Guid? contractId, Guid? lineId, string? status, CancellationToken ct = default);
    Task<RentalTimesheetDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>معاينة كشف جديد: أيام الفترة معبّأة مبدئياً ومبالغها بشروط السطر (لا يُحفظ شيء).</summary>
    Task<RentalTimesheetDto> DraftAsync(Guid contractId, Guid lineId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<RentalTimesheetDto> CreateAsync(SaveRentalTimesheetDto request, CancellationToken ct = default);
    Task<RentalTimesheetDto> UpdateAsync(Guid id, SaveRentalTimesheetDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<RentalTimesheetDto> SubmitAsync(Guid id, CancellationToken ct = default);
    Task<RentalTimesheetDto> ApproveAsync(Guid id, ApproveRentalTimesheetDto request, CancellationToken ct = default);
    Task<RentalTimesheetDto> RejectAsync(Guid id, RentalReasonDto request, CancellationToken ct = default);
    Task<RentalTimesheetDto> ReopenAsync(Guid id, RentalReasonDto request, CancellationToken ct = default);
}
