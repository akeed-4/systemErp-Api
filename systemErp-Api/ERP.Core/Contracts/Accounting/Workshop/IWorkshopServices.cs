using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>إعداد الورشة: الإعداد العام، والمراحل الفرعية، وأسباب التوقف، وتعريفات الخدمات فوق الأصناف الخدمية، وقوالب الفحص.</summary>
public interface IWorkshopSetupService
{
    Task<WorkshopSettingsDto> SettingsAsync(CancellationToken ct = default);
    Task<WorkshopSettingsDto> SaveSettingsAsync(SaveWorkshopSettingsDto request, CancellationToken ct = default);

    Task<List<WorkshopStageDto>> StagesAsync(CancellationToken ct = default);
    Task<WorkshopStageDto> CreateStageAsync(SaveWorkshopStageDto request, CancellationToken ct = default);
    Task<WorkshopStageDto> UpdateStageAsync(Guid id, SaveWorkshopStageDto request, CancellationToken ct = default);
    Task DeleteStageAsync(Guid id, CancellationToken ct = default);

    Task<List<WorkshopPauseReasonDto>> PauseReasonsAsync(CancellationToken ct = default);
    Task<WorkshopPauseReasonDto> CreatePauseReasonAsync(SaveWorkshopPauseReasonDto request, CancellationToken ct = default);
    Task<WorkshopPauseReasonDto> UpdatePauseReasonAsync(Guid id, SaveWorkshopPauseReasonDto request, CancellationToken ct = default);
    Task DeletePauseReasonAsync(Guid id, CancellationToken ct = default);

    Task<List<WorkshopServiceDto>> ServicesAsync(bool activeOnly, CancellationToken ct = default);
    Task<WorkshopServiceDto> CreateServiceAsync(SaveWorkshopServiceDto request, CancellationToken ct = default);
    Task<WorkshopServiceDto> UpdateServiceAsync(Guid id, SaveWorkshopServiceDto request, CancellationToken ct = default);
    Task DeleteServiceAsync(Guid id, CancellationToken ct = default);

    Task<List<InspectionTemplateDto>> TemplatesAsync(string? usage, CancellationToken ct = default);
    Task<InspectionTemplateDto> GetTemplateAsync(Guid id, CancellationToken ct = default);
    Task<InspectionTemplateDto> CreateTemplateAsync(SaveInspectionTemplateDto request, CancellationToken ct = default);
    Task<InspectionTemplateDto> UpdateTemplateAsync(Guid id, SaveInspectionTemplateDto request, CancellationToken ct = default);
    Task DeleteTemplateAsync(Guid id, CancellationToken ct = default);
}

/// <summary>سجل مركبات العملاء في الورشة وتاريخ صيانتها.</summary>
public interface IServiceVehicleService
{
    Task<List<ServiceVehicleDto>> ListAsync(Guid? customerId, string? search, CancellationToken ct = default);
    Task<ServiceVehicleDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ServiceVehicleDto> CreateAsync(SaveServiceVehicleDto request, CancellationToken ct = default);
    Task<ServiceVehicleDto> UpdateAsync(Guid id, SaveServiceVehicleDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<List<ServiceVehicleHistoryDto>> HistoryAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// استقبال المركبات (عدة مركبات للعميل في زيارة) وأوامر العمل: لكل مركبة أمر عمل بلقطة العميل والعدّاد والوقود والشكوى والأضرار القائمة،
/// وانتقالات الحالة الثابتة ومراحلها الفرعية وأسباب التوقف بسجلها، والإلغاء بسبب قبل الفوترة.
/// </summary>
public interface IWorkOrderService
{
    Task<List<WorkshopReceptionDto>> ReceptionsAsync(Guid? customerId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<WorkshopReceptionDto> GetReceptionAsync(Guid id, CancellationToken ct = default);
    Task<WorkshopReceptionDto> CreateReceptionAsync(SaveWorkshopReceptionDto request, CancellationToken ct = default);
    Task<WorkshopReceptionDto> UpdateReceptionAsync(Guid id, UpdateWorkshopReceptionDto request, CancellationToken ct = default);
    /// <summary>مركبة إضافية للاستقبال نفسه (أمر عمل جديد).</summary>
    Task<WorkOrderDto> AddVehicleAsync(Guid receptionId, ReceptionVehicleDto request, CancellationToken ct = default);

    Task<List<WorkOrderDto>> ListAsync(string? status, Guid? customerId, Guid? vehicleId, Guid? receptionId, CancellationToken ct = default);
    Task<WorkOrderDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<WorkOrderDto> UpdateAsync(Guid id, UpdateWorkOrderDto request, CancellationToken ct = default);
    Task<WorkOrderDto> ChangeStatusAsync(Guid id, ChangeWorkOrderStatusDto request, CancellationToken ct = default);
    Task<WorkOrderDto> CancelAsync(Guid id, WorkshopReasonDto request, CancellationToken ct = default);
    /// <summary>حذف أمر «مستلم» بلا أثر (ويُحذف الاستقبال إن لم يبق له أمر).</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<WorkOrderDto> AddDamageAsync(Guid id, SaveDamageMarkDto request, CancellationToken ct = default);
    Task<WorkOrderDto> UpdateDamageAsync(Guid id, Guid damageId, SaveDamageMarkDto request, CancellationToken ct = default);
    Task<WorkOrderDto> DeleteDamageAsync(Guid id, Guid damageId, CancellationToken ct = default);
}

/// <summary>
/// العمل على أمر العمل (W2): الفحوص من القوالب واقتراح الأسطر منها، والأسطر بأسعارها، والتقديرات المجمّدة وموافقة العميل الجزئية، والاستثناء
/// الموثق لتنفيذ سطر غير موافق. التنفيذ (الساعات والصرف والخدمة الخارجية) يسأل <c>CanExecute</c> لكل سطر.
/// </summary>
public interface IWorkOrderWorkService
{
    Task<WorkOrderWorkDto> GetAsync(Guid workOrderId, CancellationToken ct = default);

    Task<WorkOrderInspectionDto> CreateInspectionAsync(Guid workOrderId, CreateInspectionDto request, CancellationToken ct = default);
    Task<WorkOrderInspectionDto> SaveInspectionAsync(Guid workOrderId, Guid inspectionId, SaveInspectionResultsDto request, CancellationToken ct = default);
    Task<WorkOrderInspectionDto> CompleteInspectionAsync(Guid workOrderId, Guid inspectionId, CancellationToken ct = default);
    Task DeleteInspectionAsync(Guid workOrderId, Guid inspectionId, CancellationToken ct = default);

    Task<WorkOrderWorkDto> AddLineAsync(Guid workOrderId, SaveWorkOrderLineDto request, CancellationToken ct = default);
    Task<WorkOrderWorkDto> UpdateLineAsync(Guid workOrderId, Guid lineId, SaveWorkOrderLineDto request, CancellationToken ct = default);
    Task<WorkOrderWorkDto> DeleteLineAsync(Guid workOrderId, Guid lineId, CancellationToken ct = default);
    Task<WorkOrderWorkDto> ChangeExecutionAsync(Guid workOrderId, Guid lineId, ChangeLineExecutionDto request, CancellationToken ct = default);

    Task<WorkOrderEstimateDto> CreateEstimateAsync(Guid workOrderId, CreateEstimateDto request, CancellationToken ct = default);
    Task<WorkOrderEstimateDto> SendEstimateAsync(Guid workOrderId, Guid estimateId, CancellationToken ct = default);
    Task<WorkOrderEstimateDto> RespondEstimateAsync(Guid workOrderId, Guid estimateId, RespondEstimateDto request, CancellationToken ct = default);
    Task DeleteEstimateAsync(Guid workOrderId, Guid estimateId, CancellationToken ct = default);

    Task<WorkOrderWorkDto> AddExceptionAsync(Guid workOrderId, Guid lineId, CreateExecutionExceptionDto request, CancellationToken ct = default);
}

/// <summary>
/// تنفيذ أمر العمل (W3) بعد بوابة <c>CanExecute</c>: ساعات الفنيين والتوقف (قيد العمالة على تحت التنفيذ عند الإغلاق)، وحجز القطع وصرفها
/// وإرجاعها (حركة مخزون وقيد)، والخدمات الخارجية عند الموردين. لوحة الفني عبر <see cref="OpenLogsAsync"/>.
/// </summary>
public interface IWorkOrderExecutionService
{
    Task<WorkOrderExecutionDto> GetAsync(Guid workOrderId, CancellationToken ct = default);
    /// <summary>السجلات المفتوحة الآن (لوحة الفنيين)، اختيارياً لفني واحد.</summary>
    Task<List<TimeLogDto>> OpenLogsAsync(Guid? employeeId, CancellationToken ct = default);

    Task<TimeLogDto> StartLogAsync(Guid workOrderId, SaveTimeLogDto request, CancellationToken ct = default);
    Task<TimeLogDto> StopLogAsync(Guid workOrderId, Guid logId, StopTimeLogDto request, CancellationToken ct = default);
    Task DeleteLogAsync(Guid workOrderId, Guid logId, CancellationToken ct = default);

    Task<WorkOrderExecutionDto> ReserveAsync(Guid workOrderId, ReservePartDto request, CancellationToken ct = default);
    Task<WorkOrderExecutionDto> ReleaseAsync(Guid workOrderId, Guid reservationId, CancellationToken ct = default);
    Task<WorkOrderExecutionDto> IssueAsync(Guid workOrderId, IssuePartDto request, CancellationToken ct = default);
    Task<WorkOrderExecutionDto> ReturnAsync(Guid workOrderId, ReturnPartDto request, CancellationToken ct = default);

    Task<WorkOrderExecutionDto> AddExternalAsync(Guid workOrderId, SaveExternalServiceDto request, CancellationToken ct = default);
    Task<WorkOrderExecutionDto> CompleteExternalAsync(Guid workOrderId, Guid serviceId, CompleteExternalServiceDto request, CancellationToken ct = default);
    Task<WorkOrderExecutionDto> CancelExternalAsync(Guid workOrderId, Guid serviceId, WorkshopReasonDto request, CancellationToken ct = default);
}

/// <summary>
/// إنهاء أمر العمل (W4): الفوترة عبر محرك فواتير المبيعات (مرجع <c>workshop_work_order</c> تُلغى من الأمر وحده) مع نقل التكلفة من تحت
/// التنفيذ، وإلغاء الفاتورة بمرتجع كامل يعكس التكلفة، والتسليم بعدّاده وإنشاء ضمانات الأسطر، والإغلاق، والضمانات ومطالباتها.
/// </summary>
public interface IWorkOrderBillingService
{
    Task<WorkOrderBillingPreviewDto> PreviewAsync(Guid workOrderId, CancellationToken ct = default);
    Task<WorkOrderDto> InvoiceAsync(Guid workOrderId, InvoiceWorkOrderDto request, CancellationToken ct = default);
    Task<WorkOrderDto> CancelInvoiceAsync(Guid workOrderId, WorkshopReasonDto request, CancellationToken ct = default);
    Task<WorkOrderDto> DeliverAsync(Guid workOrderId, DeliverWorkOrderDto request, CancellationToken ct = default);
    Task<WorkOrderDto> CloseAsync(Guid workOrderId, CancellationToken ct = default);

    Task<List<WarrantyDto>> WarrantiesAsync(Guid? vehicleId, Guid? workOrderId, CancellationToken ct = default);
    Task<WorkOrderDto> ClaimWarrantyAsync(Guid warrantyId, ClaimWarrantyDto request, CancellationToken ct = default);
    Task<WarrantyDto> VoidWarrantyAsync(Guid warrantyId, WorkshopReasonDto request, CancellationToken ct = default);
}

/// <summary>
/// تقارير الورشة (W5): لوحة الآن (بشاشة أوامر العمل)، وربحية الأوامر والفنيين والقطع (بشاشة رؤية التكلفة المقيدة). تقرأ ولا تخزن.
/// </summary>
public interface IWorkshopReportService
{
    Task<WorkshopDashboardDto> DashboardAsync(CancellationToken ct = default);
    Task<WorkshopProfitabilityDto> ProfitabilityAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<List<TechnicianPerformanceRowDto>> TechniciansAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<List<PartProfitRowDto>> PartsAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<WorkshopTeamDto> TeamAsync(DateTime from, DateTime to, CancellationToken ct = default);
}

/// <summary>التخطيط والجدولة: المنصات، وإسناد أوامر العمل لمنصة وفني ونافذة زمنية، ولوحة الجدول.</summary>
public interface IWorkshopPlanningService
{
    Task<List<WorkshopBayDto>> BaysAsync(CancellationToken ct = default);
    Task<WorkshopBayDto> CreateBayAsync(SaveWorkshopBayDto request, CancellationToken ct = default);
    Task<WorkshopBayDto> UpdateBayAsync(Guid id, SaveWorkshopBayDto request, CancellationToken ct = default);
    Task DeleteBayAsync(Guid id, CancellationToken ct = default);
    Task<WorkOrderDto> PlanAsync(Guid workOrderId, PlanWorkOrderDto request, CancellationToken ct = default);
    Task<WorkshopScheduleDto> ScheduleAsync(DateTime from, DateTime to, Guid? warehouseId, CancellationToken ct = default);
}

/// <summary>المواعيد قبل الاستقبال. الوصول يربط الموعد بأمر العمل المستلم لنفس العميل.</summary>
public interface IWorkshopAppointmentService
{
    Task<List<WorkshopAppointmentDto>> ListAsync(DateTime? from, DateTime? to, string? status, Guid? customerId, CancellationToken ct = default);
    Task<WorkshopAppointmentDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<WorkshopAppointmentDto> CreateAsync(SaveWorkshopAppointmentDto request, CancellationToken ct = default);
    Task<WorkshopAppointmentDto> UpdateAsync(Guid id, SaveWorkshopAppointmentDto request, CancellationToken ct = default);
    Task<WorkshopAppointmentDto> SetStatusAsync(Guid id, AppointmentStatusDto request, CancellationToken ct = default);
    Task<WorkshopAppointmentDto> ArriveAsync(Guid id, AppointmentArriveDto request, CancellationToken ct = default);
    Task<WorkshopAppointmentDto> CancelAsync(Guid id, WorkshopReasonDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>متابعات ما بعد التسليم وتذكيرات الصيانة الدورية.</summary>
public interface IWorkshopFollowUpService
{
    Task<List<WorkshopFollowUpDto>> ListAsync(string? status, bool dueOnly, Guid? vehicleId, CancellationToken ct = default);
    Task<WorkshopFollowUpDto> CreateAsync(SaveWorkshopFollowUpDto request, CancellationToken ct = default);
    Task<WorkshopFollowUpDto> CompleteAsync(Guid id, CompleteFollowUpDto request, CancellationToken ct = default);
    Task<WorkshopFollowUpDto> CancelAsync(Guid id, WorkshopReasonDto request, CancellationToken ct = default);
}

/// <summary>تجربة الفني (الجوال): أوامره المسندة وسجل عمله هو فقط، بهوية الموظف المربوط بالمستخدم الحالي.</summary>
public interface IWorkshopTechnicianService
{
    Task<MyJobsDto> MyJobsAsync(CancellationToken ct = default);
    Task<MyJobsDto> StartAsync(Guid workOrderId, MyJobStartDto request, CancellationToken ct = default);
    Task<MyJobsDto> StopAsync(StopTimeLogDto request, CancellationToken ct = default);
    Task<MyJobsDto> CompleteLineAsync(Guid workOrderId, Guid lineId, CancellationToken ct = default);
}
