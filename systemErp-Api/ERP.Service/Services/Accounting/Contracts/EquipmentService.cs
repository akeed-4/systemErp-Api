using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using ASt = ERP.Core.Models.Accounting.EquipmentAssignmentStatuses;
using K = ERP.Core.Models.Accounting.EquipmentLogKinds;
using RSt = ERP.Core.Models.Accounting.EquipmentRentalStatuses;
using St = ERP.Core.Models.Accounting.EquipmentStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// المعدات والآليات فوق ما هو قائم، بلا تكرار محاسبي:
/// <list type="bullet">
/// <item>المملوكة تُربط بأصلها الثابت (قيمته وإهلاكه من الأصول الثابتة)، والمستأجرة بمؤجرها وعقد إيجارها.</item>
/// <item>الإسناد لمشروع ينشئ مورد المشروع (<see cref="ProjectResource"/>) عبر خدمة المشاريع، فساعاتها وتوقفها من التقرير اليومي،
/// وتكلفة ساعتها تُحمَّل على مركز المشروع عند اعتماده (المملوكة قيد تحميل، والمستأجرة استحقاق إيجار يُسوَّى بفاتورة المؤجر).</item>
/// <item>الوقود والمصاريف تُقيَّد من هنا فقط إن حُدِّد حساب السداد؛ وإلا فهي للمتابعة ومقيَّدة من مستندها.</item>
/// </list>
/// </summary>
public class EquipmentService : IEquipmentService
{
    internal const string LogSource = "equipment_log";
    private readonly ErpDbContext _db;
    private readonly IConstructionProjectService _projects;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public EquipmentService(ErpDbContext db, IConstructionProjectService projects, IAccountingPostingService posting, INumberSequenceService numbers,
        ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _projects = projects; _posting = posting; _numbers = numbers; _tx = tx; _user = user; _audit = audit;
    }

    // =====================================================================
    // السجل
    // =====================================================================

    public async Task<List<EquipmentDto>> ListAsync(CancellationToken ct = default)
        => await ToDtosAsync(await _db.Set<Equipment>().AsNoTracking().OrderBy(e => e.Code).ToListAsync(ct), ct);

    public async Task<EquipmentDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await ToDtosAsync(new List<Equipment> { await FindAsync(_db, id, track: false, ct) }, ct)).Single();

    public Task<EquipmentDto> CreateAsync(SaveEquipmentDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var e = new Equipment();
            await ApplyAsync(e, request, isNew: true, token);
            _db.Add(e);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_CREATED", nameof(Equipment), e.Id.ToString(), $"معدة {e.Code} • {e.NameAr} • {e.Ownership}", token);
            return await GetAsync(e.Id, token);
        }, ct);

    public Task<EquipmentDto> UpdateAsync(Guid id, SaveEquipmentDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var e = await FindAsync(_db, id, track: true, token);
            await ApplyAsync(e, request, isNew: false, token);
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await FindAsync(_db, id, track: true, ct);
        if (await _db.Set<EquipmentAssignment>().AnyAsync(a => a.EquipmentId == id, ct)
            || await _db.Set<EquipmentLog>().AnyAsync(l => l.EquipmentId == id, ct)
            || await _db.Set<EquipmentMaintenanceOrder>().AnyAsync(o => o.EquipmentId == id, ct)
            || await _db.Set<EquipmentRentalContract>().AnyAsync(r => r.EquipmentId == id, ct))
            throw new ConflictException($"المعدة {e.Code} عليها إسناد أو سجل تشغيل أو صيانة أو عقد إيجار؛ أخرجها من الخدمة بدل حذفها.");
        // تسعيرات الإيراد إعداد للمعدة وحدها: تُحذف معها
        _db.RemoveRange(await _db.Set<EquipmentRevenueRate>().Where(r => r.EquipmentId == id).ToListAsync(ct));
        _db.Remove(e);
        await _db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Equipment e, SaveEquipmentDto r, bool isNew, CancellationToken ct)
    {
        var errors = new List<string>();
        var name = r.NameAr?.Trim() ?? string.Empty;
        if (name.Length == 0) errors.Add("اسم المعدة مطلوب.");
        if (!EquipmentOwnerships.IsValid(r.Ownership)) errors.Add("ملكية المعدة غير صحيحة (مملوكة أو مستأجرة).");
        var owned = r.Ownership == EquipmentOwnerships.Owned;
        // طريقة التحميل منفصلة عن الملكية: المملوكة بالإهلاك أو بسعر داخلي، والمستأجرة بإيجارها الفعلي وحده
        var method = string.IsNullOrWhiteSpace(r.CostMethod)
            ? (isNew || e.Ownership != r.Ownership ? EquipmentCostMethods.DefaultFor(r.Ownership) : e.CostMethod)
            : r.CostMethod.Trim();
        if (EquipmentOwnerships.IsValid(r.Ownership) && !EquipmentCostMethods.IsValidFor(r.Ownership, method))
            errors.Add(owned ? "طريقة تكلفة المعدة المملوكة: الإهلاك الفعلي أو سعر الإيجار الداخلي." : "المعدة المستأجرة تكلفتها من عقد إيجارها الفعلي.");
        var internalRate = method == EquipmentCostMethods.InternalRate;
        // الإيجار الداخلي بأساسه (ساعة/يوم/شهر)؛ طريقة الإهلاك بلا سعر ملكية؛ المستأجرة سعرها من عقد إيجارها
        var basis = string.IsNullOrWhiteSpace(r.RateBasis) ? (owned ? ProjectRateBases.Hour : ProjectRateBases.Month) : r.RateBasis.Trim();
        if (owned && !internalRate) basis = ProjectRateBases.Hour;
        if (!ProjectRateBases.IsValid(basis)) errors.Add("أساس السعر غير صحيح (ساعة، يوم، شهر).");
        if (method == EquipmentCostMethods.Depreciation && r.FixedAssetId == null)
            errors.Add("طريقة الإهلاك تحتاج ربط المعدة بأصلها الثابت: منه يُوزَّع الإهلاك الفعلي على المشاريع.");
        if (r.Rate < 0 || r.FuelPerHour < 0 || r.MaintenancePerHour < 0 || r.OperatorPerHour < 0) errors.Add("تكاليف المعدة لا تكون سالبة.");
        var hoursPerDay = r.HoursPerDay ?? 8m;
        var daysPerMonth = r.DaysPerMonth ?? 26m;
        if (hoursPerDay is <= 0 or > 24) errors.Add("ساعات اليوم بين 1 و24.");
        if (daysPerMonth is <= 0 or > 31) errors.Add("أيام الشهر بين 1 و31.");
        if (r.ManufactureYear is < 1950 or > 2100) errors.Add("سنة الصنع غير صحيحة.");
        if (r.MeterHours < 0) errors.Add("قراءة العدّاد لا تكون سالبة.");
        if (r.ServiceIntervalHours < 0 || r.ServiceIntervalDays < 0) errors.Add("فترة الصيانة الوقائية لا تكون سالبة.");

        if (!isNew && e.Ownership != r.Ownership && await _db.Set<EquipmentAssignment>().AnyAsync(a => a.EquipmentId == e.Id, ct))
            errors.Add("لا تُغيَّر ملكية معدة سبق إسنادها لمشروع.");
        // الطريقة تُنسخ لمورد المشروع عند الإسناد؛ تغييرها أثناء إسناد نشط يخلط طريقتين في الفترة نفسها (تحميل مزدوج)
        var methodChanged = !isNew && e.CostMethod != method;
        if (methodChanged && await _db.Set<EquipmentAssignment>().AnyAsync(a => a.EquipmentId == e.Id && a.Status == ASt.Active, ct))
            errors.Add("لا تُغيَّر طريقة تكلفة معدة مسندة لمشروع: أرجعها أولاً، فيسري التغيير من إسنادها التالي.");

        if (owned && r.FixedAssetId is Guid assetId)
        {
            var asset = await _db.Set<FixedAsset>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, ct);
            if (asset == null) errors.Add("الأصل الثابت غير موجود.");
            else if (asset.DisposedAt != null) errors.Add($"الأصل {asset.AssetCode} مستبعد؛ لا يُربط بمعدة.");
            else if (await _db.Set<Equipment>().AnyAsync(x => x.FixedAssetId == assetId && x.Id != e.Id, ct))
                errors.Add($"الأصل {asset.AssetCode} مربوط بمعدة أخرى (أصل واحد لمعدة واحدة).");
            // إهلاك الأصل يُرحَّل على مركزه: إن كان مركز مشروع لحمل المشروعَ الإهلاكَ مع سعره الداخلي أو حصته (تحميل مزدوج)
            else if ((isNew || methodChanged || e.FixedAssetId != assetId) && asset.CostCenterId is Guid assetCenter
                && await _db.Set<CommercialContract>().AnyAsync(c => c.CostCenterId == assetCenter, ct))
                errors.Add($"مركز تكلفة الأصل {asset.AssetCode} مركز مشروع: إهلاكه يُحمَّل عليه مباشرة فيتكرر مع تكلفة المعدة. اجعل مركز الأصل مركز المعدات.");
        }
        if (!owned && r.SupplierId is Guid supplierId && !await _db.Set<Supplier>().AnyAsync(s => s.Id == supplierId, ct)) errors.Add("المؤجر غير موجود.");
        if (r.LibraryResourceId is Guid libId && !await _db.Set<ConstructionResource>().AnyAsync(x => x.Id == libId, ct)) errors.Add("مورد المكتبة غير موجود.");
        if (r.CostCenterId is Guid ccId && !await _db.Set<CostCenter>().AnyAsync(c => c.Id == ccId && c.IsActive, ct)) errors.Add("مركز التكلفة غير موجود أو موقوف.");

        var code = Trim(r.Code, 32);
        if (code != null && await _db.Set<Equipment>().AnyAsync(x => x.Code == code && x.Id != e.Id, ct)) errors.Add($"رمز المعدة «{code}» مستخدم.");

        if (!string.IsNullOrWhiteSpace(r.Status) && r.Status != e.Status)
        {
            if (!St.Manual.Contains(r.Status)) errors.Add("حالة المعدة تُضبط يدوياً إلى: متاحة، خارج الخدمة، مُعادة للمؤجر.");
            else if (e.Status is St.Assigned or St.Maintenance or St.Rented) errors.Add("المعدة مسندة لمشروع أو مؤجَّرة لعميل أو في الصيانة؛ أرجعها أو أكمل أمر العمل قبل تغيير حالتها.");
            else if (r.Status == St.Returned && owned) errors.Add("«مُعادة للمؤجر» للمعدة المستأجرة فقط.");
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        e.Code = code ?? (isNew || string.IsNullOrEmpty(e.Code) ? await NextCodeAsync(ct) : e.Code);
        e.NameAr = name[..Math.Min(name.Length, 200)]; e.NameEn = Trim(r.NameEn, 200); e.Category = Trim(r.Category, 100);
        e.Brand = Trim(r.Brand, 100); e.Model = Trim(r.Model, 100); e.ManufactureYear = r.ManufactureYear;
        e.SerialNumber = Trim(r.SerialNumber, 100); e.PlateNumber = Trim(r.PlateNumber, 50); e.Capacity = Trim(r.Capacity, 100);
        e.Specifications = Trim(r.Specifications, 1000);
        e.Ownership = r.Ownership; e.CostMethod = method; e.FixedAssetId = owned ? r.FixedAssetId : null; e.SupplierId = owned ? null : r.SupplierId;
        e.LibraryResourceId = r.LibraryResourceId; e.CostCenterId = r.CostCenterId;
        // طريقة الإهلاك: لا سعر ملكية بالساعة (يبقى المدخل صفراً فلا يُحمَّل خطأً لو غُيِّرت الطريقة لاحقاً دون سعر)
        e.RateBasis = basis; e.Rate = owned && !internalRate ? 0 : r.Rate; e.FuelPerHour = r.FuelPerHour; e.MaintenancePerHour = r.MaintenancePerHour;
        e.OperatorPerHour = r.OperatorPerHour; e.HoursPerDay = hoursPerDay; e.DaysPerMonth = daysPerMonth;
        if (r.MeterHours is decimal meter) e.MeterHours = meter;
        e.ServiceIntervalHours = r.ServiceIntervalHours > 0 ? r.ServiceIntervalHours : null;
        e.ServiceIntervalDays = r.ServiceIntervalDays > 0 ? r.ServiceIntervalDays : null;
        e.Notes = Trim(r.Notes, 1000);
        if (!string.IsNullOrWhiteSpace(r.Status)) e.Status = r.Status;
    }

    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var n = await _db.Set<Equipment>().CountAsync(ct);
        while (true)
        {
            var code = $"EQ-{++n:0000}";
            if (!await _db.Set<Equipment>().AnyAsync(x => x.Code == code, ct)) return code;
        }
    }

    public async Task<EquipmentOptionsDto> OptionsAsync(CancellationToken ct = default)
    {
        var boqTypes = await _db.Set<ContractTypeDefinition>().AsNoTracking()
            .Where(t => t.Billing == ContractBilling.BoqCertificate).Select(t => t.Code).ToListAsync(ct);
        var linked = await _db.Set<Equipment>().AsNoTracking().Where(e => e.FixedAssetId != null).Select(e => e.FixedAssetId!.Value).ToListAsync(ct);
        var settings = await _db.Set<AccountingSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        return new EquipmentOptionsDto
        {
            // المشاريع كما في قائمة مشاريع المقاولات: عقد عميل بجدول كميات، لا مناقصة قبل ترسيتها ولا دراسة قبل عقدها، وغير مغلق
            Projects = await _db.Set<CommercialContract>().AsNoTracking()
                .Where(c => boqTypes.Contains(c.ContractType) && c.PartyType != "supplier"
                    && (c.TenderStatus == null || c.TenderStatus == ContractTenderStatuses.Awarded)
                    && (c.FeasibilityStatus == null || c.FeasibilityStatus == FeasibilityStatuses.Contracted)
                    && c.Stage != ContractStageDefinition.Closed && c.Stage != ContractStageDefinition.Cancelled)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new EquipmentOptionDto { Id = c.Id, Code = c.ContractNumber, Name = c.ProjectName ?? c.Title, Detail = c.ProjectLocation })
                .ToListAsync(ct),
            Employees = await _db.Set<Employee>().AsNoTracking().Where(e => e.Status == EmployeeStatuses.Active).OrderBy(e => e.NameAr)
                .Select(e => new EquipmentOptionDto { Id = e.Id, Code = e.Code, Name = e.NameAr, Detail = e.JobTitle }).ToListAsync(ct),
            FixedAssets = await _db.Set<FixedAsset>().AsNoTracking().Where(a => a.DisposedAt == null).OrderBy(a => a.AssetCode)
                .Select(a => new EquipmentOptionDto { Id = a.Id, Code = a.AssetCode, Name = a.NameAr, Detail = linked.Contains(a.Id) ? "linked" : null })
                .ToListAsync(ct),
            Categories = await _db.Set<Equipment>().AsNoTracking().Where(e => e.Category != null).Select(e => e.Category!).Distinct().OrderBy(c => c).ToListAsync(ct),
            DefaultExpenseAccountCode = settings?.ProjectEquipmentCostAccountCode,
        };
    }

    // =====================================================================
    // الإسناد والتحويل والإرجاع
    // =====================================================================

    public async Task<List<EquipmentAssignmentDto>> AssignmentsAsync(Guid? equipmentId, Guid? contractId, bool activeOnly, CancellationToken ct = default)
    {
        var q = _db.Set<EquipmentAssignment>().AsNoTracking().AsQueryable();
        if (equipmentId is Guid eq) q = q.Where(a => a.EquipmentId == eq);
        if (contractId is Guid c) q = q.Where(a => a.ContractId == c);
        if (activeOnly) q = q.Where(a => a.Status == ASt.Active);
        return await AssignmentDtosAsync(await q.OrderByDescending(a => a.FromDate).ThenByDescending(a => a.CreatedAt).ToListAsync(ct), ct);
    }

    public Task<EquipmentAssignmentDto> AssignAsync(AssignEquipmentDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var e = await FindAsync(_db, request.EquipmentId, track: true, token);
            if (request.LibraryResourceId is Guid lib && !await _db.Set<ConstructionResource>().AnyAsync(x => x.Id == lib, token))
                throw new ValidationFailedException("مورد المكتبة غير موجود.");
            var a = await OpenAssignmentAsync(e, request.ContractId, request.SiteName, request.OperatorEmployeeId,
                request.FromDate?.Date ?? SaudiTime.Today, request.Notes, null, token, request.LibraryResourceId);
            return (await AssignmentDtosAsync(new List<EquipmentAssignment> { a }, token)).Single();
        }, ct);

    public async Task<EquipmentAssignmentDto> UpdateAssignmentAsync(Guid assignmentId, UpdateEquipmentAssignmentDto request, CancellationToken ct = default)
    {
        var a = await FindAssignmentAsync(assignmentId, ct);
        if (a.Status != ASt.Active) throw new ConflictException("الإسناد منتهٍ؛ لا يُعدَّل.");
        await EnsureOperatorAsync(request.OperatorEmployeeId, ct);
        a.SiteName = Trim(request.SiteName, 200); a.OperatorEmployeeId = request.OperatorEmployeeId; a.Notes = Trim(request.Notes, 500);
        await _db.SaveChangesAsync(ct);
        return (await AssignmentDtosAsync(new List<EquipmentAssignment> { a }, ct)).Single();
    }

    public Task<EquipmentAssignmentDto> ReturnAsync(Guid assignmentId, ReturnEquipmentDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var a = await FindAssignmentAsync(assignmentId, token);
            var e = await FindAsync(_db, a.EquipmentId, track: true, token);
            await CloseAssignmentAsync(a, e, ASt.Returned, request.Date?.Date ?? SaudiTime.Today, request.MeterReading, request.Notes, token);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_RETURNED", nameof(Equipment), e.Id.ToString(), $"إرجاع المعدة {e.Code} من المشروع • {a.ToDate:yyyy-MM-dd}", token);
            return (await AssignmentDtosAsync(new List<EquipmentAssignment> { a }, token)).Single();
        }, ct);

    public Task<EquipmentAssignmentDto> TransferAsync(Guid assignmentId, TransferEquipmentDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var a = await FindAssignmentAsync(assignmentId, token);
            if (a.ContractId == request.ContractId) throw new ValidationFailedException("المعدة مسندة لهذا المشروع نفسه؛ اختر مشروعاً آخر.");
            var e = await FindAsync(_db, a.EquipmentId, track: true, token);
            if (e.Status == St.Maintenance) throw new ConflictException($"المعدة {e.Code} في الصيانة؛ أكمل أمر العمل قبل تحويلها.");
            var date = request.Date?.Date ?? SaudiTime.Today;
            await CloseAssignmentAsync(a, e, ASt.Transferred, date, request.MeterReading, request.Notes, token);
            // بند الخطة الذي كان المورد يغطيه يبقى (المشروع الجديد يطابقه إن كان في خطته)
            var library = a.ProjectResourceId is Guid prev
                ? await _db.Set<ProjectResource>().AsNoTracking().Where(r => r.Id == prev).Select(r => r.LibraryResourceId).FirstOrDefaultAsync(token) : null;
            var next = await OpenAssignmentAsync(e, request.ContractId, request.SiteName, request.OperatorEmployeeId ?? a.OperatorEmployeeId, date, request.Notes, a.Id, token, library);
            return (await AssignmentDtosAsync(new List<EquipmentAssignment> { next }, token)).Single();
        }, ct);

    private async Task<EquipmentAssignment> OpenAssignmentAsync(Equipment e, Guid contractId, string? site, Guid? operatorId, DateTime date, string? notes,
        Guid? transferredFrom, CancellationToken ct, Guid? libraryResourceId = null)
    {
        if (e.Status != St.Available)
            throw new ConflictException(e.Status switch
            {
                St.Assigned => $"المعدة {e.Code} مسندة لمشروع؛ حوّلها أو أرجعها أولاً.",
                St.Maintenance => $"المعدة {e.Code} في الصيانة؛ أكمل أمر العمل قبل إسنادها.",
                St.Returned => $"المعدة {e.Code} مُعادة للمؤجر.",
                _ => $"المعدة {e.Code} خارج الخدمة.",
            });
        if (await _db.Set<EquipmentAssignment>().AnyAsync(a => a.EquipmentId == e.Id && a.Status == ASt.Active, ct))
            throw new ConflictException($"للمعدة {e.Code} إسناد نشط.");
        // الإسناد مفتوح من تاريخه: لا يتداخل مع حجز تأجير للعملاء (يقفل صف المعدة فلا يسبقه حجز متزامن)
        await RentalAvailability.EnsureFreeForAssignmentAsync(_db, e.Id, date, ct);
        await EnsureOperatorAsync(operatorId, ct);

        var owned = e.Ownership == EquipmentOwnerships.Owned;
        var resource = new ProjectResourceDto
        {
            Kind = owned ? ProjectResourceKinds.OwnedEquipment : ProjectResourceKinds.RentedEquipment,
            Name = e.NameAr, Code = e.Code, Category = e.Category, LibraryResourceId = libraryResourceId ?? e.LibraryResourceId, HoursPerDay = e.HoursPerDay, DaysPerMonth = e.DaysPerMonth,
            Notes = $"من سجل المعدات {e.Code}",
        };
        // لقطة طريقة التحميل على المورد: ساعات هذا الإسناد تُحمَّل بها وحدها (لا سعر داخلي وحصة إهلاك معاً)
        resource.EquipmentCostMethod = EquipmentCostMethods.IsValidFor(e.Ownership, e.CostMethod) ? e.CostMethod : EquipmentCostMethods.DefaultFor(e.Ownership);
        if (owned)
        {
            // السعر الداخلي بأساسه (ساعة/يوم/شهر)؛ طريقة الإهلاك بلا سعر ملكية (حصتها من الإهلاك الفعلي شهرياً) وتبقى تكاليف التشغيل بالساعة
            var internalRate = resource.EquipmentCostMethod == EquipmentCostMethods.InternalRate;
            resource.FixedAssetId = e.FixedAssetId;
            resource.RateBasis = internalRate ? e.RateBasis : ProjectRateBases.Hour;
            resource.Rate = internalRate ? e.Rate : 0;
            resource.FuelPerHour = e.FuelPerHour; resource.MaintenancePerHour = e.MaintenancePerHour; resource.OperatorPerHour = e.OperatorPerHour;
        }
        else
        {
            // المستأجرة: سعر الإيجار وشروط فوترته من عقدها الساري وحده (استحقاق المؤجر = تكلفة الساعة)، فلا يدخله وقود ولا مشغّل
            var rental = await _db.Set<EquipmentRentalContract>().AsNoTracking()
                .Where(c => c.EquipmentId == e.Id && c.Status == RSt.Active && c.StartDate <= date && (c.EndDate == null || c.EndDate >= date))
                .OrderByDescending(c => c.StartDate).FirstOrDefaultAsync(ct)
                ?? throw new ConflictException($"لا عقد إيجار سارٍ للمعدة {e.Code} في {date:yyyy-MM-dd}؛ فعّل عقد إيجارها قبل إسنادها.");
            resource.SupplierId = rental.SupplierId; resource.RateBasis = rental.RateBasis; resource.Rate = rental.Rate;
            resource.HoursPerDay = rental.HoursPerDay; resource.DaysPerMonth = rental.DaysPerMonth;
            resource.RentalStart = rental.StartDate; resource.RentalEnd = rental.EndDate;
            resource.IdleBillingPercent = rental.IdleBillingPercent; resource.StandbyBillingPercent = rental.StandbyBillingPercent;
        }
        // مورد المشروع بقواعد خدمة المشاريع نفسها (العقد قابل للتعديل، والتكلفة موجبة)، والجديد هو ما لم يكن قبل الحفظ
        var before = await _db.Set<ProjectResource>().Where(r => r.ContractId == contractId).Select(r => r.Id).ToListAsync(ct);
        var saved = _projects is ConstructionProjectService concrete
            ? await concrete.SaveRegistryResourceAsync(contractId, resource, ct)
            : await _projects.SaveResourceAsync(contractId, null, resource, ct);
        var resourceId = saved.Where(r => r.Id.HasValue && !before.Contains(r.Id.Value)).Select(r => r.Id!.Value).Single();

        var a = new EquipmentAssignment
        {
            EquipmentId = e.Id, ContractId = contractId, ProjectResourceId = resourceId, SiteName = Trim(site, 200), OperatorEmployeeId = operatorId,
            FromDate = date, Status = ASt.Active, MeterStart = e.MeterHours, TransferredFromId = transferredFrom, Notes = Trim(notes, 500),
        };
        _db.Add(a);
        e.Status = St.Assigned;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(transferredFrom == null ? "EQUIPMENT_ASSIGNED" : "EQUIPMENT_TRANSFERRED", nameof(Equipment), e.Id.ToString(),
            $"{(transferredFrom == null ? "إسناد" : "تحويل")} المعدة {e.Code} لمشروع {contractId} • {date:yyyy-MM-dd}", ct);
        return a;
    }

    private async Task CloseAssignmentAsync(EquipmentAssignment a, Equipment e, string status, DateTime date, decimal? meter, string? notes, CancellationToken ct)
    {
        if (a.Status != ASt.Active) throw new ConflictException("الإسناد منتهٍ من قبل.");
        if (date < a.FromDate.Date) throw new ValidationFailedException($"تاريخ الإرجاع قبل بداية الإسناد ({a.FromDate:yyyy-MM-dd}).");
        if (meter is decimal m)
        {
            if (m < (a.MeterStart ?? 0)) throw new ValidationFailedException($"قراءة العدّاد أقل من قراءة بداية الإسناد ({a.MeterStart:0.##}).");
            e.MeterHours = Math.Max(e.MeterHours, m);
        }
        a.Status = status; a.ToDate = date; a.MeterEnd = meter; a.ReturnNotes = Trim(notes, 500);
        // مورد المشروع يبقى لتقاريره السابقة ويتوقف فلا يُختار في تقرير جديد
        if (a.ProjectResourceId is Guid rid && await _db.Set<ProjectResource>().FirstOrDefaultAsync(r => r.Id == rid, ct) is { } resource)
            resource.IsActive = false;
        if (e.Status == St.Assigned) e.Status = St.Available;
        else if (e.Status == St.Maintenance)
            // في الصيانة: تعود «متاحة» لا «مسندة» عند إكمال أمر العمل
            foreach (var order in await _db.Set<EquipmentMaintenanceOrder>()
                         .Where(o => o.EquipmentId == e.Id && o.Status == EquipmentMaintenanceStatuses.InProgress).ToListAsync(ct))
                order.StatusBefore = St.Available;
    }

    private async Task EnsureOperatorAsync(Guid? operatorId, CancellationToken ct)
    {
        if (operatorId is Guid id && !await _db.Set<Employee>().AnyAsync(x => x.Id == id && x.Status == EmployeeStatuses.Active, ct))
            throw new ValidationFailedException("المشغّل غير موجود في شؤون الموظفين أو غير فعّال.");
    }

    private async Task<EquipmentAssignment> FindAssignmentAsync(Guid id, CancellationToken ct)
        => await _db.Set<EquipmentAssignment>().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("إسناد المعدة غير موجود.");

    private async Task<List<EquipmentAssignmentDto>> AssignmentDtosAsync(List<EquipmentAssignment> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var equipment = await EquipmentNamesAsync(list.Select(a => a.EquipmentId), ct);
        var contracts = await ContractNamesAsync(list.Select(a => a.ContractId), ct);
        var operators = await EmployeeNamesAsync(list.Select(a => a.OperatorEmployeeId), ct);
        var resourceIds = list.Where(a => a.ProjectResourceId != null).Select(a => a.ProjectResourceId!.Value).ToList();
        var usage = (await ApprovedUsage().Where(u => resourceIds.Contains(u.ResourceId))
                .GroupBy(u => u.ResourceId).Select(g => new { g.Key, Hours = g.Sum(x => x.Hours), Idle = g.Sum(x => x.Idle), Cost = g.Sum(x => x.Cost) }).ToListAsync(ct))
            .ToDictionary(x => x.Key);
        return list.Select(a =>
        {
            var eq = equipment.GetValueOrDefault(a.EquipmentId);
            var c = contracts.GetValueOrDefault(a.ContractId);
            var u = a.ProjectResourceId is Guid rid ? usage.GetValueOrDefault(rid) : null;
            return new EquipmentAssignmentDto
            {
                Id = a.Id, EquipmentId = a.EquipmentId, EquipmentCode = eq?.Code ?? string.Empty, EquipmentName = eq?.NameAr ?? string.Empty,
                Ownership = eq?.Ownership ?? string.Empty, ContractId = a.ContractId, ContractNumber = c?.Number, ProjectName = c?.Name,
                ProjectResourceId = a.ProjectResourceId, SiteName = a.SiteName, OperatorEmployeeId = a.OperatorEmployeeId,
                OperatorName = a.OperatorEmployeeId is Guid op ? operators.GetValueOrDefault(op) : null,
                FromDate = a.FromDate, ToDate = a.ToDate, Status = a.Status, MeterStart = a.MeterStart, MeterEnd = a.MeterEnd, Notes = a.Notes,
                ReturnNotes = a.ReturnNotes, ReportedHours = u?.Hours ?? 0, IdleHours = u?.Idle ?? 0, ChargedCost = u?.Cost ?? 0,
            };
        }).ToList();
    }

    // =====================================================================
    // سجل التشغيل
    // =====================================================================

    public async Task<List<EquipmentLogDto>> LogsAsync(Guid? equipmentId, string? kind, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var q = _db.Set<EquipmentLog>().AsNoTracking().AsQueryable();
        if (equipmentId is Guid eq) q = q.Where(l => l.EquipmentId == eq);
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(l => l.Kind == kind);
        if (from is DateTime f) q = q.Where(l => l.Date >= f.Date);
        if (to is DateTime t) q = q.Where(l => l.Date <= t.Date);
        return await LogDtosAsync(await q.OrderByDescending(l => l.Date).ThenByDescending(l => l.CreatedAt).Take(2000).ToListAsync(ct), ct);
    }

    public Task<EquipmentLogDto> AddLogAsync(SaveEquipmentLogDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var e = await FindAsync(_db, r.EquipmentId, track: true, token);
            if (!K.All.Contains(r.Kind)) throw new ValidationFailedException("نوع سطر السجل غير صحيح.");
            var date = r.Date?.Date ?? SaudiTime.Today;
            var errors = new List<string>();
            if (date > SaudiTime.Today) errors.Add("تاريخ السجل لا يكون في المستقبل.");
            if (r.Hours < 0 || r.Quantity < 0 || r.Amount < 0 || r.MeterReading < 0) errors.Add("القيم لا تكون سالبة.");
            if (r.Hours > 24) errors.Add("الساعات في اليوم لا تتجاوز 24.");
            if (r.Kind is K.Operation or K.Downtime && r.Hours <= 0) errors.Add("أدخل عدد الساعات.");
            if (r.Kind == K.Fuel && r.Quantity <= 0 && r.Amount <= 0) errors.Add("أدخل كمية الوقود أو قيمته.");
            if (r.Kind == K.Expense && r.Amount <= 0) errors.Add("أدخل قيمة المصروف.");
            if (r.Kind == K.Expense && string.IsNullOrWhiteSpace(r.Reason)) errors.Add("أدخل نوع المصروف.");
            if (r.Kind == K.Downtime && string.IsNullOrWhiteSpace(r.Reason)) errors.Add("أدخل سبب التوقف.");
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

            var assignment = await _db.Set<EquipmentAssignment>().AsNoTracking()
                .Where(a => a.EquipmentId == e.Id && a.FromDate <= date && (a.ToDate == null || a.ToDate >= date))
                .OrderByDescending(a => a.FromDate).FirstOrDefaultAsync(token);
            // ساعات المعدة المسندة مصدرها التقرير اليومي للمشروع: تسجيلها هنا أيضاً يكررها في التقارير والصيانة الوقائية
            if (r.Kind == K.Operation && assignment != null)
                throw new ConflictException($"المعدة {e.Code} مسندة لمشروع في {date:yyyy-MM-dd}؛ ساعات تشغيلها تُسجَّل في التقرير اليومي للمشروع.");

            var hasAmount = K.HasAmount(r.Kind);
            var log = new EquipmentLog
            {
                EquipmentId = e.Id, Kind = r.Kind, Date = date, ContractId = assignment?.ContractId,
                // الكمية: لترات الوقود، أو وحدات عمل التشغيل (رحلات/مهام) لإيرادها بأساس الرحلة أو المهمة
                Hours = r.Kind is K.Operation or K.Downtime ? r.Hours : 0, Quantity = r.Kind is K.Fuel or K.Operation ? r.Quantity : 0,
                Amount = hasAmount ? DocumentPricing.Round(r.Amount) : 0, MeterReading = r.MeterReading, Reason = Trim(r.Reason, 200), Notes = Trim(r.Notes, 500),
                CreatedBy = _user.Name,
            };
            if (r.MeterReading is decimal reading) e.MeterHours = Math.Max(e.MeterHours, reading);
            else if (r.Kind == K.Operation) e.MeterHours += r.Hours;

            _db.Add(log);
            await _db.SaveChangesAsync(token);
            if (hasAmount && log.Amount > 0 && !string.IsNullOrWhiteSpace(r.CreditAccountCode))
            {
                // الافتراضي: المستأجرة على المشروع (إيجارها وحده يُستحق بالساعة)، والمملوكة على مركزها (وقودها وصيانتها ضمن سعر ساعتها المحمّل)
                var charge = (r.ChargeToProject ?? e.Ownership == EquipmentOwnerships.Rented) && assignment != null;
                var posted = await PostCostAsync(_db, _posting, new CostPosting(e, charge ? assignment!.ContractId : null, r.ExpenseAccountCode, r.CreditAccountCode,
                    log.Amount, date, LogSource, log.Id, e.Code,
                    $"{(r.Kind == K.Fuel ? "وقود" : "مصروف")} المعدة {e.Code} - {e.NameAr}{(log.Reason == null ? "" : " - " + log.Reason)}"), token);
                log.ChargeToProject = posted.ChargedToProject; log.ExpenseAccountCode = posted.ExpenseAccount; log.CreditAccountCode = posted.CreditAccount;
                log.CostCenterId = posted.CostCenterId; log.JournalEntryId = posted.JournalEntryId;
                await _db.SaveChangesAsync(token);
            }
            return (await LogDtosAsync(new List<EquipmentLog> { log }, token)).Single();
        }, ct);

    public Task DeleteLogAsync(Guid logId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var log = await _db.Set<EquipmentLog>().FirstOrDefaultAsync(l => l.Id == logId, token) ?? throw new NotFoundException("سطر السجل غير موجود.");
            if (log.JournalEntryId is Guid entry) await _posting.ReverseAsync(entry, "حذف سطر من سجل تشغيل المعدة", token);
            _db.Remove(log);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_LOG_DELETED", nameof(EquipmentLog), logId.ToString(), $"حذف سطر {log.Kind} • {log.Date:yyyy-MM-dd} • {log.Amount:0.00}", token);
        }, ct);

    private async Task<List<EquipmentLogDto>> LogDtosAsync(List<EquipmentLog> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var equipment = await EquipmentNamesAsync(list.Select(l => l.EquipmentId), ct);
        var contracts = await ContractNamesAsync(list.Where(l => l.ContractId != null).Select(l => l.ContractId!.Value), ct);
        return list.Select(l => new EquipmentLogDto
        {
            Id = l.Id, EquipmentId = l.EquipmentId, EquipmentCode = equipment.GetValueOrDefault(l.EquipmentId)?.Code ?? string.Empty,
            EquipmentName = equipment.GetValueOrDefault(l.EquipmentId)?.NameAr ?? string.Empty, Kind = l.Kind, Date = l.Date, ContractId = l.ContractId,
            ProjectName = l.ContractId is Guid c ? contracts.GetValueOrDefault(c)?.Name : null, Hours = l.Hours, Quantity = l.Quantity, Amount = l.Amount,
            MeterReading = l.MeterReading, Reason = l.Reason, Notes = l.Notes, ChargeToProject = l.ChargeToProject, ExpenseAccountCode = l.ExpenseAccountCode,
            CreditAccountCode = l.CreditAccountCode, JournalEntryId = l.JournalEntryId, Posted = l.JournalEntryId != null, CreatedBy = l.CreatedBy, CreatedAt = l.CreatedAt,
        }).ToList();
    }

    // ---------- قيد تكلفة المعدة (وقود/مصروف/صيانة) ----------

    internal sealed record CostPosting(Equipment Equipment, Guid? ProjectContractId, string? ExpenseAccount, string? CreditAccount, decimal Amount, DateTime Date,
        string SourceType, Guid SourceId, string SourceNumber, string Note, IReadOnlyList<PostingLine>? ExtraCredits = null);

    internal sealed record CostPosted(Guid JournalEntryId, string ExpenseAccount, string? CreditAccount, Guid? CostCenterId, bool ChargedToProject);

    /// <summary>
    /// مدين حساب المصروف (المحدد، وإلا «تكلفة معدات المشاريع» من الإعدادات) بمركز المشروع إن حُمِّل عليه وله مركز، وإلا بمركز المعدة؛
    /// دائن حساب السداد بالمبلغ، مع أسطر دائنة إضافية (مخزون قطع الغيار) تُضاف لمدين المصروف.
    /// </summary>
    internal static async Task<CostPosted> PostCostAsync(ErpDbContext db, IAccountingPostingService posting, CostPosting p, CancellationToken ct)
    {
        await ContractAccountsSetup.EnsureMissingAsync(db, ct);
        var settings = await PostingAccounts.SettingsAsync(db, ct);
        var expense = string.IsNullOrWhiteSpace(p.ExpenseAccount)
            ? settings.ContractAccount(nameof(AccountingSettings.ProjectEquipmentCostAccountCode)) : p.ExpenseAccount.Trim();
        var credit = string.IsNullOrWhiteSpace(p.CreditAccount) ? null : p.CreditAccount.Trim();
        foreach (var code in new[] { expense, credit }.Where(c => c != null))
            if (!await db.Set<Account>().AnyAsync(a => a.Code == code && a.IsActive, ct))
                throw new ValidationFailedException($"الحساب {code} غير موجود في دليل الحسابات أو موقوف.");
        if (credit == expense) throw new ValidationFailedException("حساب السداد هو حساب المصروف نفسه.");

        Guid? center = p.Equipment.CostCenterId;
        var charged = false;
        if (p.ProjectContractId is Guid contractId)
        {
            var projectCenter = await db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == contractId).Select(c => c.CostCenterId).FirstOrDefaultAsync(ct);
            if (projectCenter != null) { center = projectCenter; charged = true; }
        }
        var extra = p.ExtraCredits ?? Array.Empty<PostingLine>();
        var total = DocumentPricing.Round((credit == null ? 0 : p.Amount) + extra.Sum(l => l.Credit));
        if (total <= 0) throw new ValidationFailedException("لا مبلغ يُقيَّد.");
        var lines = new List<PostingLine> { new(expense, total, 0, p.Note, center) };
        if (credit != null && p.Amount > 0) lines.Add(new(credit, 0, DocumentPricing.Round(p.Amount), p.Note, center));
        lines.AddRange(extra);
        var result = await posting.PostAsync(new GenericPostingRequest
        {
            Date = p.Date, Description = p.Note, SourceType = p.SourceType, SourceId = p.SourceId, SourceNumber = p.SourceNumber, Lines = lines,
        }, ct);
        return new CostPosted(result.JournalEntryId, expense, credit, center, charged);
    }

    // =====================================================================
    // عقود الإيجار
    // =====================================================================

    public async Task<List<EquipmentRentalContractDto>> RentalsAsync(Guid? equipmentId, CancellationToken ct = default)
    {
        var q = _db.Set<EquipmentRentalContract>().AsNoTracking().AsQueryable();
        if (equipmentId is Guid eq) q = q.Where(r => r.EquipmentId == eq);
        return await RentalDtosAsync(await q.OrderByDescending(r => r.StartDate).ThenByDescending(r => r.CreatedAt).ToListAsync(ct), ct);
    }

    public Task<EquipmentRentalContractDto> CreateRentalAsync(SaveEquipmentRentalContractDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var r = new EquipmentRentalContract { Status = RSt.Draft };
            await ApplyRentalAsync(r, request, token);
            r.ContractNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "equipment_rental", DefaultPrefix = "ERC-", Date = r.StartDate,
                Exists = (n, c) => _db.Set<EquipmentRentalContract>().AnyAsync(x => x.ContractNumber == n, c),
            }, token);
            _db.Add(r);
            await _db.SaveChangesAsync(token);
            return (await RentalDtosAsync(new List<EquipmentRentalContract> { r }, token)).Single();
        }, ct);

    public Task<EquipmentRentalContractDto> UpdateRentalAsync(Guid rentalId, SaveEquipmentRentalContractDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var r = await FindRentalAsync(rentalId, token);
            if (r.Status == RSt.Closed) throw new ConflictException($"عقد الإيجار {r.ContractNumber} مغلق؛ لا يُعدَّل.");
            if (r.Status == RSt.Draft) await ApplyRentalAsync(r, request, token);
            else
            {
                // الساري: المدة والتأمين والشروط فقط (السعر وشروط الفوترة مثبّتة على مورد المشروع والتقارير)
                if (request.EndDate is DateTime end && end.Date < r.StartDate.Date) throw new ValidationFailedException("نهاية الإيجار قبل بدايته.");
                r.EndDate = request.EndDate?.Date; r.DepositAmount = Math.Max(0, request.DepositAmount); r.Terms = Trim(request.Terms, 2000);
                var resourceIds = await _db.Set<EquipmentAssignment>().Where(a => a.EquipmentId == r.EquipmentId && a.Status == ASt.Active && a.ProjectResourceId != null)
                    .Select(a => a.ProjectResourceId!.Value).ToListAsync(token);
                foreach (var resource in await _db.Set<ProjectResource>().Where(x => resourceIds.Contains(x.Id)).ToListAsync(token)) resource.RentalEnd = r.EndDate;
            }
            await _db.SaveChangesAsync(token);
            return (await RentalDtosAsync(new List<EquipmentRentalContract> { r }, token)).Single();
        }, ct);

    public Task<EquipmentRentalContractDto> ActivateRentalAsync(Guid rentalId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var r = await FindRentalAsync(rentalId, token);
            if (r.Status != RSt.Draft) throw new ConflictException($"عقد الإيجار {r.ContractNumber} ليس مسودة.");
            if (r.Rate <= 0) throw new ValidationFailedException("أدخل سعر الإيجار قبل تفعيل العقد.");
            var other = await _db.Set<EquipmentRentalContract>().AsNoTracking()
                .Where(x => x.EquipmentId == r.EquipmentId && x.Id != r.Id && x.Status == RSt.Active
                    && (x.EndDate == null || x.EndDate >= r.StartDate) && (r.EndDate == null || x.StartDate <= r.EndDate))
                .Select(x => x.ContractNumber).FirstOrDefaultAsync(token);
            if (other != null) throw new ConflictException($"للمعدة عقد إيجار سارٍ ({other}) يتداخل مع هذه المدة؛ أغلقه أولاً.");
            var e = await FindAsync(_db, r.EquipmentId, track: true, token);
            r.Status = RSt.Active;
            e.SupplierId = r.SupplierId;
            if (e.Status == St.Returned) e.Status = St.Available;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_RENTAL_ACTIVATED", nameof(EquipmentRentalContract), r.Id.ToString(), $"تفعيل عقد الإيجار {r.ContractNumber} • المعدة {e.Code}", token);
            return (await RentalDtosAsync(new List<EquipmentRentalContract> { r }, token)).Single();
        }, ct);

    public Task<EquipmentRentalContractDto> CloseRentalAsync(Guid rentalId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var r = await FindRentalAsync(rentalId, token);
            if (r.Status != RSt.Active) throw new ConflictException($"عقد الإيجار {r.ContractNumber} ليس سارياً.");
            var e = await FindAsync(_db, r.EquipmentId, track: true, token);
            if (await _db.Set<EquipmentAssignment>().AnyAsync(a => a.EquipmentId == e.Id && a.Status == ASt.Active, token))
                throw new ConflictException($"المعدة {e.Code} مسندة لمشروع؛ أرجعها قبل إغلاق عقد إيجارها.");
            r.Status = RSt.Closed; r.ClosedAt = SaudiTime.Now;
            if (r.EndDate == null || r.EndDate > SaudiTime.Today) r.EndDate = SaudiTime.Today < r.StartDate ? r.StartDate : SaudiTime.Today;
            // لا عقد سارٍ آخر: المعدة تُعاد للمؤجر
            if (e.Status == St.Available && !await _db.Set<EquipmentRentalContract>().AnyAsync(x => x.EquipmentId == e.Id && x.Id != r.Id && x.Status == RSt.Active, token))
                e.Status = St.Returned;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_RENTAL_CLOSED", nameof(EquipmentRentalContract), r.Id.ToString(), $"إغلاق عقد الإيجار {r.ContractNumber} • المعدة {e.Code}", token);
            return (await RentalDtosAsync(new List<EquipmentRentalContract> { r }, token)).Single();
        }, ct);

    public async Task DeleteRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        var r = await FindRentalAsync(rentalId, ct);
        if (r.Status != RSt.Draft) throw new ConflictException($"عقد الإيجار {r.ContractNumber} ليس مسودة؛ أغلقه بدل حذفه.");
        _db.Remove(r);
        await _db.SaveChangesAsync(ct);
    }

    private async Task ApplyRentalAsync(EquipmentRentalContract r, SaveEquipmentRentalContractDto d, CancellationToken ct)
    {
        var errors = new List<string>();
        var e = await _db.Set<Equipment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == d.EquipmentId, ct);
        if (e == null) errors.Add("المعدة غير موجودة.");
        else if (e.Ownership != EquipmentOwnerships.Rented) errors.Add($"المعدة {e.Code} مملوكة؛ عقد الإيجار للمعدات المستأجرة.");
        if (!await _db.Set<Supplier>().AnyAsync(s => s.Id == d.SupplierId, ct)) errors.Add("المؤجر (المورد) مطلوب.");
        if (d.StartDate == default) errors.Add("تاريخ بداية الإيجار مطلوب.");
        if (d.EndDate is DateTime end && end.Date < d.StartDate.Date) errors.Add("نهاية الإيجار قبل بدايته.");
        if (!ProjectRateBases.IsValid(d.RateBasis)) errors.Add("أساس السعر غير صحيح (ساعة، يوم، شهر).");
        if (d.Rate < 0 || d.DepositAmount < 0) errors.Add("السعر والتأمين لا يكونان سالبين.");
        var idle = d.IdleBillingPercent ?? 100m;
        var standby = d.StandbyBillingPercent ?? 0m;
        if (idle is < 0 or > 100 || standby is < 0 or > 100) errors.Add("نسب فوترة التوقف والاستعداد بين 0 و100.");
        var hoursPerDay = d.HoursPerDay ?? e?.HoursPerDay ?? 8m;
        var daysPerMonth = d.DaysPerMonth ?? e?.DaysPerMonth ?? 26m;
        if (hoursPerDay is <= 0 or > 24) errors.Add("ساعات اليوم بين 1 و24.");
        if (daysPerMonth is <= 0 or > 31) errors.Add("أيام الشهر بين 1 و31.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        r.EquipmentId = d.EquipmentId; r.SupplierId = d.SupplierId; r.StartDate = d.StartDate.Date; r.EndDate = d.EndDate?.Date;
        r.RateBasis = d.RateBasis; r.Rate = d.Rate; r.IdleBillingPercent = idle; r.StandbyBillingPercent = standby;
        r.HoursPerDay = hoursPerDay; r.DaysPerMonth = daysPerMonth; r.DepositAmount = d.DepositAmount; r.Terms = Trim(d.Terms, 2000);
    }

    private async Task<EquipmentRentalContract> FindRentalAsync(Guid id, CancellationToken ct)
        => await _db.Set<EquipmentRentalContract>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("عقد الإيجار غير موجود.");

    private async Task<List<EquipmentRentalContractDto>> RentalDtosAsync(List<EquipmentRentalContract> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var equipmentIds = list.Select(r => r.EquipmentId).Distinct().ToList();
        var equipment = await EquipmentNamesAsync(equipmentIds, ct);
        var supplierIds = list.Select(r => r.SupplierId).Distinct().ToList();
        var suppliers = await _db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        // المستحق من التقارير اليومية على موارد إسنادات المعدة ضمن مدة العقد (المصدر استحقاق الإيجار القائم)
        var links = await _db.Set<EquipmentAssignment>().AsNoTracking().Where(a => equipmentIds.Contains(a.EquipmentId) && a.ProjectResourceId != null)
            .Select(a => new { a.EquipmentId, ResourceId = a.ProjectResourceId!.Value }).ToListAsync(ct);
        var resourceIds = links.Select(l => l.ResourceId).ToList();
        var accruals = await _db.Set<SiteRentalAccrual>().AsNoTracking().Where(a => resourceIds.Contains(a.ProjectResourceId))
            .Select(a => new { a.ProjectResourceId, a.ReportDate, a.Amount, a.SettledAmount, a.ClosedAmount }).ToListAsync(ct);
        var equipmentOfResource = links.ToDictionary(l => l.ResourceId, l => l.EquipmentId);
        var today = SaudiTime.Today;
        return list.Select(r =>
        {
            var mine = accruals.Where(a => equipmentOfResource[a.ProjectResourceId] == r.EquipmentId && a.ReportDate >= r.StartDate
                && (r.EndDate == null || a.ReportDate <= r.EndDate)).ToList();
            var eq = equipment.GetValueOrDefault(r.EquipmentId);
            return new EquipmentRentalContractDto
            {
                Id = r.Id, ContractNumber = r.ContractNumber, EquipmentId = r.EquipmentId, EquipmentCode = eq?.Code ?? string.Empty,
                EquipmentName = eq?.NameAr ?? string.Empty, SupplierId = r.SupplierId, SupplierName = suppliers.GetValueOrDefault(r.SupplierId),
                StartDate = r.StartDate, EndDate = r.EndDate, RateBasis = r.RateBasis, Rate = r.Rate, IdleBillingPercent = r.IdleBillingPercent,
                StandbyBillingPercent = r.StandbyBillingPercent, HoursPerDay = r.HoursPerDay, DaysPerMonth = r.DaysPerMonth, DepositAmount = r.DepositAmount,
                Status = r.Status, Terms = r.Terms, ClosedAt = r.ClosedAt, CreatedAt = r.CreatedAt,
                HourlyCost = ResourceCostRules.HourlyCost(r.RateBasis, r.Rate, r.HoursPerDay, r.DaysPerMonth, 0, 1).Hourly,
                DaysRemaining = r.Status == RSt.Active && r.EndDate is DateTime end ? (end.Date - today).Days : null,
                AccruedAmount = mine.Sum(a => a.Amount), SettledAmount = mine.Sum(a => a.SettledAmount),
                OpenAmount = mine.Sum(a => a.Amount - a.SettledAmount - a.ClosedAmount),
            };
        }).ToList();
    }

    // =====================================================================
    // اللوحة وتقرير التكلفة
    // =====================================================================

    public async Task<EquipmentDashboardDto> DashboardAsync(CancellationToken ct = default)
    {
        var list = await ListAsync(ct);
        var today = SaudiTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var report = await CostReportAsync(monthStart, today, ct);
        var orders = await _db.Set<EquipmentMaintenanceOrder>().AsNoTracking()
            .Where(o => o.Status == EquipmentMaintenanceStatuses.Open || o.Status == EquipmentMaintenanceStatuses.InProgress)
            .Select(o => new { o.EquipmentId, o.OrderNumber, o.ScheduledDate }).ToListAsync(ct);
        var rentals = await _db.Set<EquipmentRentalContract>().AsNoTracking().Where(r => r.Status == RSt.Active)
            .Select(r => new { r.EquipmentId, r.ContractNumber, r.EndDate }).ToListAsync(ct);
        var byId = list.ToDictionary(e => e.Id);
        var alerts = new List<EquipmentAlertDto>();
        EquipmentAlertDto Alert(string kind, Guid equipmentId, string? reference, DateTime? date, decimal? value)
        {
            var e = byId.GetValueOrDefault(equipmentId);
            return new EquipmentAlertDto { Kind = kind, EquipmentId = equipmentId, EquipmentCode = e?.Code ?? string.Empty, EquipmentName = e?.NameAr ?? string.Empty, Reference = reference, Date = date, Value = value };
        }
        alerts.AddRange(list.Where(e => e.ServiceDue).Select(e => Alert("service_due", e.Id, null, e.NextServiceDate, e.HoursToService)));
        alerts.AddRange(rentals.Where(r => r.EndDate is DateTime end && end < today).Select(r => Alert("rental_expired", r.EquipmentId, r.ContractNumber, r.EndDate, null)));
        alerts.AddRange(rentals.Where(r => r.EndDate is DateTime end && end >= today && end <= today.AddDays(30))
            .Select(r => Alert("rental_expiring", r.EquipmentId, r.ContractNumber, r.EndDate, (r.EndDate!.Value - today).Days)));
        alerts.AddRange(orders.Where(o => o.ScheduledDate < today).Select(o => Alert("order_overdue", o.EquipmentId, o.OrderNumber, o.ScheduledDate, null)));

        var usable = list.Count(e => e.Status is St.Available or St.Assigned);
        var assigned = list.Count(e => e.Status == St.Assigned);
        return new EquipmentDashboardDto
        {
            Total = list.Count, Owned = list.Count(e => e.Ownership == EquipmentOwnerships.Owned), Rented = list.Count(e => e.Ownership == EquipmentOwnerships.Rented),
            ByStatus = list.GroupBy(e => e.Status).Select(g => new EquipmentCountDto { Key = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToList(),
            ByCategory = list.GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "—" : e.Category!)
                .Select(g => new EquipmentCountDto { Key = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(12).ToList(),
            ActiveAssignments = assigned, OpenOrders = orders.Count, ServiceDue = list.Count(e => e.ServiceDue), ActiveRentals = rentals.Count,
            UtilizationPercent = usable == 0 ? 0 : Math.Round(assigned * 100m / usable, 1),
            MonthHours = report.Equipment.Sum(r => r.WorkHours), MonthFuelCost = report.Equipment.Sum(r => r.FuelCost),
            MonthExpenses = report.Equipment.Sum(r => r.Expenses), MonthMaintenanceCost = report.Equipment.Sum(r => r.MaintenanceCost),
            MonthChargedToProjects = report.Equipment.Sum(r => r.ChargedToProjects), MonthDowntimeHours = report.Equipment.Sum(r => r.DowntimeHours),
            Alerts = alerts.OrderBy(a => a.Date ?? DateTime.MaxValue).Take(50).ToList(),
        };
    }

    public async Task<EquipmentCostReportDto> CostReportAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var today = SaudiTime.Today;
        var end = to?.Date ?? today;
        var start = from?.Date ?? new DateTime(end.Year, end.Month, 1);
        if (end < start) throw new ValidationFailedException("نهاية الفترة قبل بدايتها.");

        var equipment = await _db.Set<Equipment>().AsNoTracking().OrderBy(e => e.Code).ToListAsync(ct);
        var links = await _db.Set<EquipmentAssignment>().AsNoTracking().Where(a => a.ProjectResourceId != null)
            .Select(a => new { a.EquipmentId, ResourceId = a.ProjectResourceId!.Value }).ToListAsync(ct);
        var equipmentOfResource = links.GroupBy(l => l.ResourceId).ToDictionary(g => g.Key, g => g.First().EquipmentId);
        var resourceIds = equipmentOfResource.Keys.ToList();

        // ساعات وتكلفة التقارير اليومية المعتمدة (المحمّل على المشاريع)، مجمّعة بالمورد والمشروع
        var usage = await ApprovedUsage().Where(u => resourceIds.Contains(u.ResourceId) && u.Date >= start && u.Date <= end)
            .GroupBy(u => new { u.ResourceId, u.ContractId })
            .Select(g => new { g.Key.ResourceId, g.Key.ContractId, Hours = g.Sum(x => x.Hours), Idle = g.Sum(x => x.Idle), Cost = g.Sum(x => x.Cost) }).ToListAsync(ct);
        var rental = await _db.Set<SiteRentalAccrual>().AsNoTracking()
            .Where(a => resourceIds.Contains(a.ProjectResourceId) && a.ReportDate >= start && a.ReportDate <= end)
            .GroupBy(a => a.ProjectResourceId).Select(g => new { ResourceId = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var logs = await _db.Set<EquipmentLog>().AsNoTracking().Where(l => l.Date >= start && l.Date <= end)
            .Select(l => new { l.EquipmentId, l.Kind, l.ContractId, l.Hours, l.Quantity, l.Amount, l.ChargeToProject, Posted = l.JournalEntryId != null }).ToListAsync(ct);
        var startStamp = start;
        var endStamp = end.AddDays(1);
        var orders = await _db.Set<EquipmentMaintenanceOrder>().AsNoTracking()
            .Where(o => o.Status == EquipmentMaintenanceStatuses.Completed && o.CompletedAt >= startStamp && o.CompletedAt < endStamp)
            .Select(o => new { o.EquipmentId, o.ContractId, o.TotalCost, o.DowntimeHours, o.ChargeToProject, Posted = o.JournalEntryId != null }).ToListAsync(ct);
        // الإهلاك المرحّل لأصول المعدات في أشهر الفترة (من الأصول الثابتة، بلا إعادة حساب)
        string fromPeriod = start.ToString("yyyy-MM"), toPeriod = end.ToString("yyyy-MM");
        var assetIds = equipment.Where(e => e.FixedAssetId != null).Select(e => e.FixedAssetId!.Value).ToList();
        var depreciation = (await _db.Set<FixedAssetDepreciation>().AsNoTracking()
                .Where(d => assetIds.Contains(d.FixedAssetId) && string.Compare(d.Period, fromPeriod) >= 0 && string.Compare(d.Period, toPeriod) <= 0)
                .GroupBy(d => d.FixedAssetId).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Amount);
        // طريقة الإهلاك: حصص المشاريع من الإهلاك الفعلي في أشهر الفترة (المرحّلة، لا إعادة حساب)
        var allocations = await _db.Set<EquipmentDepreciationAllocation>().AsNoTracking()
            .Where(a => string.Compare(a.Period, fromPeriod) >= 0 && string.Compare(a.Period, toPeriod) <= 0)
            .GroupBy(a => new { a.EquipmentId, a.ContractId }).Select(g => new { g.Key.EquipmentId, g.Key.ContractId, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var result = new EquipmentCostReportDto { From = start, To = end };
        foreach (var e in equipment)
        {
            var mine = usage.Where(u => equipmentOfResource[u.ResourceId] == e.Id).ToList();
            var myLogs = logs.Where(l => l.EquipmentId == e.Id).ToList();
            var myOrders = orders.Where(o => o.EquipmentId == e.Id).ToList();
            var row = new EquipmentCostRowDto
            {
                EquipmentId = e.Id, Code = e.Code, Name = e.NameAr, Category = e.Category, Ownership = e.Ownership, CostMethod = e.CostMethod,
                WorkHours = mine.Sum(u => u.Hours) + myLogs.Where(l => l.Kind == K.Operation).Sum(l => l.Hours),
                IdleHours = mine.Sum(u => u.Idle),
                DowntimeHours = myLogs.Where(l => l.Kind == K.Downtime).Sum(l => l.Hours) + myOrders.Sum(o => o.DowntimeHours),
                FuelQuantity = myLogs.Where(l => l.Kind == K.Fuel).Sum(l => l.Quantity), FuelCost = myLogs.Where(l => l.Kind == K.Fuel).Sum(l => l.Amount),
                Expenses = myLogs.Where(l => l.Kind == K.Expense).Sum(l => l.Amount), MaintenanceCost = myOrders.Sum(o => o.TotalCost),
                Depreciation = e.FixedAssetId is Guid asset ? depreciation.GetValueOrDefault(asset) : 0,
                RentalCost = rental.Where(r => equipmentOfResource[r.ResourceId] == e.Id).Sum(r => r.Amount),
                DepreciationAllocated = allocations.Where(a => a.EquipmentId == e.Id).Sum(a => a.Amount),
            };
            // المحمّل = بالساعة من التقارير + حصص الإهلاك؛ والفعلي يعدّ الإهلاك مرة واحدة (التوزيع نقل لا تكلفة جديدة)
            row.ChargedToProjects = mine.Sum(u => u.Cost) + row.DepreciationAllocated;
            row.ActualCost =row.FuelCost + row.Expenses + row.MaintenanceCost + row.Depreciation + row.RentalCost;
            row.Recovery = row.ChargedToProjects - row.ActualCost;
            row.CostPerHour = row.WorkHours > 0 ? Math.Round(row.ActualCost / row.WorkHours, 2) : 0;
            if (row.WorkHours != 0 || row.IdleHours != 0 || row.DowntimeHours != 0 || row.ActualCost != 0 || row.ChargedToProjects != 0) result.Equipment.Add(row);
        }

        // التوزيع على المشاريع: المحمّل بالساعة + ما حُمِّل مباشرة على مركز المشروع من وقود ومصاريف وصيانة مقيَّدة
        var direct = logs.Where(l => l.ChargeToProject && l.Posted && l.ContractId != null).Select(l => new { l.EquipmentId, ContractId = l.ContractId!.Value, Amount = l.Amount })
            .Concat(orders.Where(o => o.ChargeToProject && o.Posted && o.ContractId != null).Select(o => new { o.EquipmentId, ContractId = o.ContractId!.Value, Amount = o.TotalCost }))
            .ToList();
        var pairs = usage.Select(u => (EquipmentId: equipmentOfResource[u.ResourceId], u.ContractId))
            .Concat(direct.Select(d => (d.EquipmentId, d.ContractId)))
            .Concat(allocations.Select(a => (a.EquipmentId, a.ContractId))).Distinct().ToList();
        var contracts = await ContractNamesAsync(pairs.Select(p => p.ContractId), ct);
        var byEquipment = equipment.ToDictionary(e => e.Id);
        foreach (var (equipmentId, contractId) in pairs)
        {
            var e = byEquipment[equipmentId];
            var u = usage.Where(x => equipmentOfResource[x.ResourceId] == equipmentId && x.ContractId == contractId).ToList();
            var c = contracts.GetValueOrDefault(contractId);
            var row = new EquipmentProjectCostRowDto
            {
                ContractId = contractId, ContractNumber = c?.Number, ProjectName = c?.Name, EquipmentId = equipmentId, EquipmentCode = e.Code, EquipmentName = e.NameAr,
                Ownership = e.Ownership, CostMethod = e.CostMethod, WorkHours = u.Sum(x => x.Hours), IdleHours = u.Sum(x => x.Idle), ChargedCost = u.Sum(x => x.Cost),
                DepreciationAllocated = allocations.Where(a => a.EquipmentId == equipmentId && a.ContractId == contractId).Sum(a => a.Amount),
                DirectCost = direct.Where(d => d.EquipmentId == equipmentId && d.ContractId == contractId).Sum(d => d.Amount),
            };
            row.TotalCost = row.ChargedCost + row.DepreciationAllocated + row.DirectCost;
            result.Projects.Add(row);
        }
        result.Projects = result.Projects.OrderBy(p => p.ProjectName).ThenBy(p => p.EquipmentCode).ToList();
        return result;
    }

    // =====================================================================
    // مساعدات
    // =====================================================================

    private sealed class UsageRow
    {
        public Guid ResourceId { get; set; }
        public Guid ContractId { get; set; }
        public DateTime Date { get; set; }
        public decimal Hours { get; set; }
        public decimal Idle { get; set; }
        public decimal Cost { get; set; }
    }

    /// <summary>أسطر استخدام الموارد في التقارير اليومية المعتمدة (المصدر الوحيد لساعات المعدة المسندة وتكلفتها المحمّلة).</summary>
    private IQueryable<UsageRow> ApprovedUsage()
        => from u in _db.Set<SiteResourceUsage>().AsNoTracking()
           join r in _db.Set<SiteDailyReport>().AsNoTracking() on u.ReportId equals r.Id
           where r.Status == SiteDailyReportStatuses.Approved
           select new UsageRow
           {
               ResourceId = u.ProjectResourceId, ContractId = r.ContractId, Date = r.ReportDate, Hours = u.Hours + u.OvertimeHours,
               Idle = u.IdleHours + u.StandbyHours, Cost = u.Cost,
           };

    internal static async Task<Equipment> FindAsync(ErpDbContext db, Guid id, bool track, CancellationToken ct)
    {
        var q = db.Set<Equipment>().AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("المعدة غير موجودة.");
    }

    private sealed record ContractName(string? Number, string? Name);

    private async Task<Dictionary<Guid, ContractName>> ContractNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new() : await _db.Set<CommercialContract>().AsNoTracking().Where(c => list.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new ContractName(c.ContractNumber, c.ProjectName ?? c.Title), ct);
    }

    private async Task<Dictionary<Guid, Equipment>> EquipmentNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new() : await _db.Set<Equipment>().AsNoTracking().Where(e => list.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
    }

    private async Task<Dictionary<Guid, string>> EmployeeNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        return list.Count == 0 ? new() : await _db.Set<Employee>().AsNoTracking().Where(e => list.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
    }

    private async Task<List<EquipmentDto>> ToDtosAsync(List<Equipment> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var ids = list.Select(e => e.Id).ToList();
        var supplierIds = list.Where(e => e.SupplierId != null).Select(e => e.SupplierId!.Value).Distinct().ToList();
        var suppliers = await _db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        var centerIds = list.Where(e => e.CostCenterId != null).Select(e => e.CostCenterId!.Value).Distinct().ToList();
        var centers = await _db.Set<CostCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code + " · " + c.NameAr, ct);
        var assetIds = list.Where(e => e.FixedAssetId != null).Select(e => e.FixedAssetId!.Value).Distinct().ToList();
        var assets = await _db.Set<FixedAsset>().AsNoTracking().Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new { a.AssetCode, a.PurchaseCost, a.CurrentBookValue, a.AccumulatedDepreciation }, ct);
        var active = await _db.Set<EquipmentAssignment>().AsNoTracking().Where(a => ids.Contains(a.EquipmentId) && a.Status == ASt.Active).ToListAsync(ct);
        var contracts = await ContractNamesAsync(active.Select(a => a.ContractId), ct);
        var operators = await EmployeeNamesAsync(active.Select(a => a.OperatorEmployeeId), ct);
        var hoursSince = await HoursSinceServiceAsync(list, ct);
        var today = SaudiTime.Today;

        return list.Select(e =>
        {
            var a = active.FirstOrDefault(x => x.EquipmentId == e.Id);
            var asset = e.FixedAssetId is Guid aid ? assets.GetValueOrDefault(aid) : null;
            decimal? hoursToService = e.ServiceIntervalHours is decimal interval ? interval - hoursSince.GetValueOrDefault(e.Id) : null;
            DateTime? nextService = e.ServiceIntervalDays is int days ? (e.LastServiceDate ?? e.CreatedAt).Date.AddDays(days) : null;
            var inService = e.Status is not (St.OutOfService or St.Returned);
            return new EquipmentDto
            {
                Id = e.Id, Code = e.Code, NameAr = e.NameAr, NameEn = e.NameEn, Category = e.Category, Brand = e.Brand, Model = e.Model,
                ManufactureYear = e.ManufactureYear, SerialNumber = e.SerialNumber, PlateNumber = e.PlateNumber, Capacity = e.Capacity,
                Specifications = e.Specifications, Ownership = e.Ownership, CostMethod = e.CostMethod, FixedAssetId = e.FixedAssetId, SupplierId = e.SupplierId,
                LibraryResourceId = e.LibraryResourceId, CostCenterId = e.CostCenterId, Status = e.Status, RateBasis = e.RateBasis, Rate = e.Rate,
                FuelPerHour = e.FuelPerHour, MaintenancePerHour = e.MaintenancePerHour, OperatorPerHour = e.OperatorPerHour, HoursPerDay = e.HoursPerDay,
                DaysPerMonth = e.DaysPerMonth, MeterHours = e.MeterHours, ServiceIntervalHours = e.ServiceIntervalHours, ServiceIntervalDays = e.ServiceIntervalDays,
                LastServiceMeter = e.LastServiceMeter, LastServiceDate = e.LastServiceDate, Notes = e.Notes, CreatedAt = e.CreatedAt,
                HourlyCost = ResourceCostRules.HourlyCost(e.RateBasis, e.Rate, e.HoursPerDay, e.DaysPerMonth, e.FuelPerHour + e.MaintenancePerHour + e.OperatorPerHour, 1).Hourly,
                SupplierName = e.SupplierId is Guid sid ? suppliers.GetValueOrDefault(sid) : null,
                CostCenterName = e.CostCenterId is Guid cid ? centers.GetValueOrDefault(cid) : null,
                FixedAssetCode = asset?.AssetCode, AssetPurchaseCost = asset?.PurchaseCost, AssetBookValue = asset?.CurrentBookValue,
                AssetAccumulatedDepreciation = asset == null ? null : asset.AccumulatedDepreciation ?? 0,
                CurrentContractId = a?.ContractId, CurrentProject = a == null ? null : contracts.GetValueOrDefault(a.ContractId)?.Name, CurrentSite = a?.SiteName,
                CurrentOperator = a?.OperatorEmployeeId is Guid op ? operators.GetValueOrDefault(op) : null,
                HoursToService = hoursToService, NextServiceDate = nextService,
                ServiceDue = inService && (hoursToService <= 0 || nextService <= today),
            };
        }).ToList();
    }

    /// <summary>ساعات التشغيل منذ آخر صيانة وقائية: من التقارير اليومية المعتمدة وسجل التشغيل اليدوي (لمن له فترة صيانة بالساعات).</summary>
    private async Task<Dictionary<Guid, decimal>> HoursSinceServiceAsync(List<Equipment> list, CancellationToken ct)
    {
        var tracked = list.Where(e => e.ServiceIntervalHours > 0).ToList();
        if (tracked.Count == 0) return new();
        var ids = tracked.Select(e => e.Id).ToList();
        var links = await _db.Set<EquipmentAssignment>().AsNoTracking().Where(a => ids.Contains(a.EquipmentId) && a.ProjectResourceId != null)
            .Select(a => new { a.EquipmentId, ResourceId = a.ProjectResourceId!.Value }).ToListAsync(ct);
        var resourceIds = links.Select(l => l.ResourceId).ToList();
        var usage = await ApprovedUsage().Where(u => resourceIds.Contains(u.ResourceId))
            .GroupBy(u => new { u.ResourceId, u.Date }).Select(g => new { g.Key.ResourceId, g.Key.Date, Hours = g.Sum(x => x.Hours) }).ToListAsync(ct);
        var logs = await _db.Set<EquipmentLog>().AsNoTracking().Where(l => ids.Contains(l.EquipmentId) && l.Kind == K.Operation)
            .Select(l => new { l.EquipmentId, l.Date, l.Hours }).ToListAsync(ct);
        var equipmentOfResource = links.GroupBy(l => l.ResourceId).ToDictionary(g => g.Key, g => g.First().EquipmentId);
        return tracked.ToDictionary(e => e.Id, e =>
        {
            var since = e.LastServiceDate?.Date ?? DateTime.MinValue;
            return usage.Where(u => equipmentOfResource[u.ResourceId] == e.Id && u.Date.Date > since).Sum(u => u.Hours)
                + logs.Where(l => l.EquipmentId == e.Id && l.Date.Date > since).Sum(l => l.Hours);
        });
    }

    internal static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim() is var t && t.Length > max ? t[..max] : s.Trim();
}
