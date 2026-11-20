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
