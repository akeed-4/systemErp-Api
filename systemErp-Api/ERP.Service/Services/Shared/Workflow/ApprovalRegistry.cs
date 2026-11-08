using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.Accounting.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Core.Models.Shared.ApprovalDocumentCatalog;

namespace ERP.Service.Services.Shared;

/// <summary>عملية مستند قابلة للحجز بطلب اعتماد: نوع بياناتها وطريقة تنفيذها عبر خدمة المستند نفسها.</summary>
public sealed class ApprovalOperation
{
    public Type? PayloadType { get; init; }
    public bool RequiresId { get; init; }
    /// <summary>
    /// تُجرَّب العملية داخل معاملة تُلغى قبل حجزها: تتحقق من صحتها كما لو نُفِّذت، وتبني ملخص المعتمِد من نتيجتها الفعلية.
    /// تُعطَّل لعمليات تُرسل للهيئة بعد معاملتها (لا تُرسل وثيقة تجربة).
    /// </summary>
    public bool DryRun { get; init; } = true;
    public required Func<IServiceProvider, Guid?, object?, CancellationToken, Task<object?>> Run { get; init; }
}

/// <summary>معالج مستند في سجل الاعتماد: يحدد نوع مستند السياسة من البيانات، ويحمّل المستند، وينفّذ عملياته.</summary>
public sealed class ApprovalHandler
{
    public required string Key { get; init; }
    /// <summary>(بيانات العملية، المستند الحالي) ← مفتاح المستند في فهرس الاعتماد.</summary>
    public required Func<object?, object?, string> ResolveType { get; init; }
    public Func<IServiceProvider, Guid, CancellationToken, Task<object>>? Load { get; set; }
    public Func<object, decimal?>? Amount { get; set; }
    public Dictionary<string, ApprovalOperation> Operations { get; } = new(StringComparer.Ordinal);
}

public sealed class ApprovalHandlerBuilder<TService, TDto> where TService : notnull where TDto : class
{
    public ApprovalHandler Handler { get; }

    public ApprovalHandlerBuilder(string key, Func<object?, TDto?, string> resolveType)
        => Handler = new ApprovalHandler { Key = key, ResolveType = (payload, current) => resolveType(payload, current as TDto) };

    private static TService Svc(IServiceProvider sp) => sp.GetRequiredService<TService>();

    public ApprovalHandlerBuilder<TService, TDto> Get(Func<TService, Guid, CancellationToken, Task<TDto>> get)
    {
        Handler.Load = async (sp, id, ct) => await get(Svc(sp), id, ct);
        return this;
    }

    /// <summary>تحميل المستند لمن لا تعرض خدمته قراءة مفردة.</summary>
    public ApprovalHandlerBuilder<TService, TDto> Load(Func<IServiceProvider, Guid, CancellationToken, Task<TDto>> load)
    {
        Handler.Load = async (sp, id, ct) => await load(sp, id, ct);
        return this;
    }

    public ApprovalHandlerBuilder<TService, TDto> Amount(Func<TDto, decimal> amount)
    {
        Handler.Amount = o => o is TDto d ? amount(d) : null;
        return this;
    }

    public ApprovalHandlerBuilder<TService, TDto> Create<TIn>(string action, Func<TService, TIn, CancellationToken, Task<TDto>> run, bool dryRun = true) where TIn : class
        => Add(action, new ApprovalOperation { PayloadType = typeof(TIn), DryRun = dryRun, Run = async (sp, _, p, ct) => await run(Svc(sp), (TIn)p!, ct) });

    public ApprovalHandlerBuilder<TService, TDto> Update<TIn>(string action, Func<TService, Guid, TIn, CancellationToken, Task<TDto>> run, bool dryRun = true) where TIn : class
        => Add(action, new ApprovalOperation { PayloadType = typeof(TIn), RequiresId = true, DryRun = dryRun, Run = async (sp, id, p, ct) => await run(Svc(sp), id!.Value, (TIn)p!, ct) });

    public ApprovalHandlerBuilder<TService, TDto> Act(string action, Func<TService, Guid, CancellationToken, Task<TDto>> run, bool dryRun = true)
        => Add(action, new ApprovalOperation { RequiresId = true, DryRun = dryRun, Run = async (sp, id, _, ct) => await run(Svc(sp), id!.Value, ct) });

    public ApprovalHandlerBuilder<TService, TDto> Delete(string action, Func<TService, Guid, CancellationToken, Task> run)
        => Add(action, new ApprovalOperation { RequiresId = true, Run = async (sp, id, _, ct) => { await run(Svc(sp), id!.Value, ct); return null; } });

    private ApprovalHandlerBuilder<TService, TDto> Add(string action, ApprovalOperation op)
    {
        Handler.Operations[action] = op;
        return this;
    }
}

/// <summary>
/// سجل المستندات التي تُحجز عملياتها بطلب اعتماد (IApprovalGate) وتُنفَّذ عند الاعتماد النهائي.
/// مفتاح المعالج هو ما يمرره المتحكم للبوابة؛ ونوع المستند الناتج يجب أن يكون في ApprovalDocumentCatalog.
/// الفواتير والمرتجعات والجرد ليست هنا: لها مسار اعتماد داخل خدماتها (مسودة/طلب جرد).
/// </summary>
public static class ApprovalRegistry
{
    private static readonly Dictionary<string, ApprovalHandler> Handlers = Build().ToDictionary(h => h.Key, StringComparer.Ordinal);

    public static ApprovalHandler? Find(string key) => Handlers.TryGetValue(key, out var h) ? h : null;

    private static ApprovalHandlerBuilder<TService, TDto> For<TService, TDto>(string key, string documentType) where TService : notnull where TDto : class
        => new(key, (_, _) => documentType);

    private static IEnumerable<ApprovalHandler> Build()
    {
        // ---------------- الحسابات والخزينة ----------------
        yield return new ApprovalHandlerBuilder<IVoucherService, VoucherDto>("vouchers",
                (p, c) => ((p as CreateVoucherDto)?.Type ?? c?.Type) == VoucherType.Receipt ? "receipt_voucher" : "payment_voucher")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(v => v.Amount)
            .Create<CreateVoucherDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateVoucherDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        yield return For<IJournalService, JournalEntryDto>("journal-entries", "journal_entry")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(e => e.TotalDebit)
            .Create<CreateJournalEntryDto>(Create, (s, d, ct) => s.CreateManualAsync(d, ct))
            .Update<UpdateJournalEntryDto>(Edit, (s, id, d, ct) => s.UpdateManualAsync(id, d, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteManualAsync(id, ct))
            .Act(Reverse, (s, id, ct) => s.ReverseAsync(id, ct)).Handler;

        // ---------------- المبيعات ----------------
        yield return For<IQuotationService, QuotationDto>("quotations", "quotation")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(q => q.GrandTotal)
            .Create<CreateQuotationDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateQuotationDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        yield return new ApprovalHandlerBuilder<ICommercialOrderService, CommercialOrderDto>("commercial-orders",
                (p, c) => ((p as CreateCommercialOrderDto)?.Type ?? c?.Type) == "purchase_order" ? "purchase_order" : "sales_order")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(o => o.GrandTotal)
            .Create<CreateCommercialOrderDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateCommercialOrderDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        yield return For<IDeliveryNoteService, DeliveryNoteDto>("delivery-notes", "delivery_note")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(n => n.GrandTotal)
            .Create<CreateDeliveryNoteDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateDeliveryNoteDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        yield return For<IDeliveryNoteService, DeliveryReturnNoteDto>("delivery-returns", "delivery_return")
            .Get((s, id, ct) => s.GetReturnAsync(id, ct)).Amount(r => r.GrandTotal)
            .Create<CreateDeliveryReturnNoteDto>(Create, (s, d, ct) => s.CreateReturnAsync(d, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteReturnAsync(id, ct)).Handler;

        // ---------------- المشتريات ----------------
        yield return For<IMaterialRequisitionService, MaterialRequisitionDto>("material-requisitions", "material_requisition")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(r => r.TotalEstimatedCost)
            .Create<CreateMaterialRequisitionDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateMaterialRequisitionDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        yield return For<IPurchaseLandedCostService, PurchaseLandedCostDto>("purchase-landed-costs", "purchase_landed_cost")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(v => v.GrandTotal)
            .Create<SavePurchaseLandedCostDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<SavePurchaseLandedCostDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Act(Approve, (s, id, ct) => s.ApproveAsync(id, ct))
            .Act(Cancel, (s, id, ct) => s.CancelAsync(id, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        // ---------------- المخزون ----------------
        yield return For<IStockTransferService, StockTransferDto>("stock-transfers", "stock_transfer")
            .Get((s, id, ct) => s.GetAsync(id, ct))
            .Create<CreateStockTransferDto>(Create, (s, d, ct) => s.CreateAsync(d, ct)).Handler;

        // ---------------- العقود ----------------
        yield return For<ICommercialContractService, CommercialContractDto>("commercial-contracts", "commercial_contract")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(c => c.TotalValueWithVat)
            .Create<CreateCommercialContractDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateCommercialContractDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        // احتياجات مواد المشروع: المعرّف هو العقد (خطة واحدة لكل عقد)، والمبلغ تكلفتها التقديرية
        yield return For<IProjectMaterialService, ProjectMaterialPlanDto>("contract-material-requirements", "project_material_plan")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(p => p.EstimatedCost)
            .Act(Approve, (s, id, ct) => s.ApproveAsync(id, ct)).Handler;

        // أمر التغيير: المعرّف هو الأمر، والمبلغ أثره على قيمة العقد (القيمة المطلقة لحدود الاعتماد)
        yield return For<IContractVariationService, ContractVariationDto>("contract-variations", "contract_variation")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(v => Math.Abs(v.ValueChange))
            .Act(Approve, (s, id, ct) => s.ApproveAsync(id, ct)).Handler;

        // ---------------- الموارد البشرية ----------------
        yield return For<IPayrollService, PayrollRunDto>("payroll", "payroll")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(r => r.TotalNet)
            .Create<PayrollRunRequestDto>(Post, (s, d, ct) => s.PostAsync(d, ct))
            .Act(Approve, (s, id, ct) => s.ApproveAsync(id, ct)).Handler;

        yield return For<IEmployeeLoanService, EmployeeLoanDto>("employee-loans", "employee_loan")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(l => l.Amount)
            .Create<CreateEmployeeLoanDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<RescheduleEmployeeLoanDto>(Edit, (s, id, d, ct) => s.RescheduleAsync(id, d, ct))
            .Act(Cancel, (s, id, ct) => s.CancelAsync(id, ct)).Handler;

        yield return For<IEndOfServiceService, EndOfServiceDto>("end-of-service", "end_of_service")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(e => e.Net)
            .Create<EndOfServiceRequestDto>(Post, (s, d, ct) => s.PostAsync(d, ct)).Handler;

        // ---------------- معارض السيارات ----------------
        // الإتمام والبيع السريع والإرجاع تُصدر فواتير تُرسل للهيئة بعد معاملتها: لا تُجرَّب، ويُبنى ملخصها من العقد/البيانات
        yield return For<ICarSaleService, CarSalesContractDto>("car-sales-contracts", "car_sales_contract")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(c => c.GrandTotalWithCharges)
            .Create<CreateCarSalesContractDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateCarSalesContractDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Create<QuickSaleRequestDto>(Quick, (s, d, ct) => s.QuickSaleAsync(d, ct), dryRun: false)
            .Update<CompleteSalesContractRequestDto>(Complete, (s, id, d, ct) => s.CompleteAsync(id, d, ct), dryRun: false)
            .Update<RejectProcurementRequestDto>(Cancel, (s, id, d, ct) => s.CancelAsync(id, d.Reason, ct))
            .Update<ReturnCarSaleDto>(Return, (s, id, d, ct) => s.ReturnAsync(id, d, ct), dryRun: false)
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        yield return For<ICarProcurementService, CarProcurementOrderDto>("car-procurement-orders", "car_procurement_order")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(o => o.GrandTotal * (o.ExchangeRate > 0 ? o.ExchangeRate : 1))
            .Create<CreateCarProcurementOrderDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateCarProcurementOrderDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Create<QuickCarPurchaseRequestDto>(Quick, (s, d, ct) => s.QuickPurchaseAsync(d, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        yield return For<ILandedCostVoucherService, LandedCostVoucherDto>("landed-cost-vouchers", "landed_cost_voucher")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(v => v.BaseCostTotal)
            .Create<CreateLandedCostVoucherDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateLandedCostVoucherDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Act(Approve, (s, id, ct) => s.ApproveAsync(id, ct))
            .Act(Cancel, (s, id, ct) => s.CancelAsync(id, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        yield return For<IImportCostAdvanceService, ImportCostAdvanceDto>("import-cost-advances", "import_cost_advance")
            .Get((s, id, ct) => s.GetAsync(id, ct)).Amount(a => a.Amount * (a.ExchangeRate > 0 ? a.ExchangeRate : 1))
            .Create<CreateImportCostAdvanceDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Update<UpdateImportCostAdvanceDto>(Edit, (s, id, d, ct) => s.UpdateAsync(id, d, ct))
            .Act(Approve, (s, id, ct) => s.ApproveAsync(id, ct))
            .Act(Cancel, (s, id, ct) => s.CancelAsync(id, ct))
            .Delete(Delete, (s, id, ct) => s.DeleteAsync(id, ct)).Handler;

        yield return For<IPrepaidExpenseService, PrepaidExpenseDto>("prepaid-expenses", "prepaid_expense")
            .Load(async (sp, id, ct) => Mapper.Map<PrepaidExpenseDto>(
                await sp.GetRequiredService<ERP.Service.Data.ErpDbContext>().Set<PrepaidExpense>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new NotFoundException(Messages.PrepaidExpenseNotFound)))
            .Amount(e => e.TotalAmount)
            .Create<CreatePrepaidExpenseDto>(Create, (s, d, ct) => s.CreateAsync(d, ct))
            .Act(Cancel, (s, id, ct) => s.CancelAsync(id, ct)).Handler;
    }
}
