using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using Microsoft.Extensions.Logging;
using ERP.Service.Services.Shared;
using S = ERP.Core.Models.Shared.ContractStageDefinition;

namespace ERP.Service.Services.Accounting;

/// <summary>رموز أنواع العقود: الأكواد القديمة (قبل جدول الأنواع) تُحوَّل لنظائرها كما في ترحيل البيانات.</summary>
public static class ContractTypeCodes
{
    public static string Normalize(string? code) => (code ?? string.Empty).Trim() switch
    {
        "" => "supply_materials",
        "supply" or "normal_purchasing" or "credit_sale" or "credit_sales" => "supply_materials",
        "sla_service" or "maintenance" or "service" => "monthly_maintenance",
        "construction" => "construction_project",
        "lease" or "leasing" or "consulting" or "partnership" => "existing_contract",
        var c => c,
    };

    /// <summary>مسار مبسط: من النوع، أو لأن قيمة العقد دون حد التبسيط.</summary>
    public static bool IsSimplified(ContractTypeDefinition type, decimal contractValue)
        => type.Workflow == ContractWorkflows.Simple || (type.SimpleLimit.HasValue && contractValue < type.SimpleLimit.Value);
}

/// <summary>
/// دورة حالات العقد. <see cref="MoveAsync"/> هي الطريق الوحيد لتغيير الحالة: الانتقال من جدول ContractTransition،
/// الصلاحية (أي إجراء من إجراءاته على شاشة العقود، مع النطاق وحد الاعتماد)، السبب الإلزامي، الشروط المسمّاة، ثم السجل.
/// رسائل الرفض تذكر بالضبط ما ينقص.
/// </summary>
public class ContractWorkflowService : IContractWorkflowService
{
    private const string Screen = PermissionCatalog.CommercialContractsScreenId;
    private const string Record = ScopedRecordTypes.CommercialContractKey;
    private static readonly string[] AttachmentKinds = { "signed_copy", "guarantee", "other" };
    private const int MaxAttachments = 20;

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly INotificationService _notifications;
    private readonly ILogger<ContractWorkflowService> _log;

    public ContractWorkflowService(ErpDbContext db, ICurrentUser user, IPermissionService permissions,
        INotificationService notifications, ILogger<ContractWorkflowService> log)
    {
        _db = db; _user = user; _permissions = permissions; _notifications = notifications; _log = log;
    }

    // ---------- الحالة والخيارات ----------

    public async Task<ContractWorkflowDto> GetAsync(Guid contractId, CancellationToken ct = default)
    {
        await _permissions.EnsureRecordInScopeAsync(Record, contractId, ct);
        var c = await LoadAsync(contractId, ct, track: false);
        var type = await TypeAsync(c.ContractType, ct);
        var stage = await _db.Set<S>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == c.Stage, ct);
        var approvals = await RoundApprovalsAsync(c, ct);
        var since = c.StageChangedAt ?? c.UpdatedAt ?? c.CreatedAt;
        var days = Math.Max(0, (int)(DateTime.UtcNow - since).TotalDays);

        var dto = new ContractWorkflowDto
        {
            ContractId = c.Id, Status = c.Stage, StatusNameAr = stage?.NameAr ?? c.Stage, StatusNameEn = stage?.NameEn ?? c.Stage,
            Phase = stage?.Phase ?? string.Empty, StatusSince = since, DaysInStatus = days, SlaDays = stage?.SlaDays,
            OverdueDays = stage?.SlaDays is { } sla && days > sla ? days - sla : null,
            ContractType = c.ContractType, Workflow = type?.Workflow ?? string.Empty, Billing = type?.Billing ?? string.Empty,
            IsSimplified = c.IsSimplified, RequiresGuarantee = type?.RequiresGuarantee == true && !c.IsSimplified, ReviewRound = c.ReviewRound,
            Approvals = approvals.Select(a => new ContractApprovalDto { Kind = a.Kind, Decision = a.Decision, UserName = a.UserName, Note = a.Note, DecidedAt = a.DecidedAt }).ToList(),
        };

        if (c.Stage == S.Review)
        {
            dto.CanReviewLegal = !Approved(approvals, "legal") && await _permissions.HasPermissionAsync(Screen, ScreenAction.LegalReview, ct);
            dto.CanReviewFinancial = !Approved(approvals, "financial") && await _permissions.HasPermissionAsync(Screen, ScreenAction.FinancialReview, ct);
        }
        dto.CanEditSigning = c.Stage is S.Draft or S.Review or S.Approval
            && (await _permissions.HasPermissionAsync(Screen, ScreenAction.Approve, ct) || await _permissions.HasPermissionAsync(Screen, ScreenAction.Edit, ct));

        // الأزرار: الانتقالات من الحالة الحالية التي يملك المستخدم أحد إجراءاتها فقط، ومع كلٍّ ما ينقصه
        foreach (var t in await TransitionsFromAsync(c, ct))
        {
            if (!await CanAnyAsync(t)) continue;
            dto.Transitions.Add(new ContractTransitionOptionDto
            {
                Id = t.Id, ToStatus = t.ToStatus, NameAr = t.NameAr, NameEn = t.NameEn, RequiresNote = t.RequiresNote, Tone = t.Tone,
                Blockers = await BlockersAsync(c, t, type, approvals, ct),
            });
        }

        dto.Log = await _db.Set<ContractStatusLog>().AsNoTracking().Where(l => l.ContractId == c.Id).OrderBy(l => l.At).ThenBy(l => l.CreatedAt)
            .Select(l => new ContractStatusLogDto { FromStatus = l.FromStatus, ToStatus = l.ToStatus, UserName = l.UserName, Note = l.Note, At = l.At })
            .ToListAsync(ct);
        dto.Attachments = await _db.Set<ContractAttachment>().AsNoTracking().Where(a => a.ContractId == c.Id).OrderBy(a => a.CreatedAt)
            .Select(a => new ContractAttachmentDto { Id = a.Id, Kind = a.Kind, FileName = a.FileName, ContentType = a.ContentType, Size = a.Size, UploadedBy = a.UploadedBy, CreatedAt = a.CreatedAt })
            .ToListAsync(ct);
        return dto;
    }

    // ---------- الانتقال ----------

    public async Task<ContractWorkflowDto> MoveAsync(Guid contractId, MoveContractRequestDto request, CancellationToken ct = default)
    {
        var c = await LoadAsync(contractId, ct, track: true);
        await MoveCoreAsync(c, request.ToStatus, request.Note, ct);
        await _db.SaveChangesAsync(ct);
        await NotifyStageChangedAsync(c, request.Note, ct);
        return await GetAsync(contractId, ct);
    }

    /// <summary>
    /// قلب الانتقال: معرّف ← صلاحية (ونطاق وحد اعتماد) ← سبب ← شروط ← تنفيذ وسجل. لا يحفظ (يحفظه المستدعي في معاملته).
    /// </summary>
    private async Task MoveCoreAsync(CommercialContract c, string? toStatus, string? note, CancellationToken ct)
    {
        var to = (toStatus ?? string.Empty).Trim();
        // انتقال غير معرّف (أو لمسار غير مسار العقد) يُرفض هنا مهما كان مصدر الطلب
        var transition = (await TransitionsFromAsync(c, ct)).FirstOrDefault(t => t.ToStatus == to);
        if (transition == null)
            throw new ConflictException(string.Format(Messages.ContractTransitionNotAllowed, await StatusNameAsync(c.Stage, ct), await StatusNameAsync(to, ct)));

        await _permissions.AuthorizeAnyAsync(Record, c.Id, ActionsOf(transition), ct);

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (transition.RequiresNote && note == null)
            throw new ValidationFailedException(string.Format(Messages.ContractNoteRequired, transition.NameAr));

        var type = await TypeAsync(c.ContractType, ct);
        var blockers = await BlockersAsync(c, transition, type, await RoundApprovalsAsync(c, ct), ct);
        if (blockers.Count > 0)
            throw new ConflictException(string.Format(Messages.ContractConditionsMissing, transition.NameAr, string.Join("، ", blockers)));

        // الرجوع للمسودة يبدأ جولة مراجعة جديدة (موافقات الجولة السابقة لا تُحتسب)
        if (to == S.Draft) c.ReviewRound++;
        if (to == S.Active && c.Stage is S.Approval or S.Draft) c.SignedDate ??= SaudiTime.Today;

        _db.Add(new ContractStatusLog
        {
            ContractId = c.Id, FromStatus = c.Stage, ToStatus = to, TransitionId = transition.Id, Note = note, At = DateTime.UtcNow,
            UserId = _user.UserId, UserName = _user.Name ?? "system",
        });
        c.Stage = to;
        c.Status = CommercialContractService.StatusFor(to);
        c.StageChangedAt = DateTime.UtcNow;
    }

    // ---------- المراجعة ----------

    public async Task<ContractWorkflowDto> ReviewAsync(Guid contractId, ContractReviewRequestDto request, CancellationToken ct = default)
    {
        var kind = (request.Kind ?? string.Empty).Trim().ToLowerInvariant();
        var decision = (request.Decision ?? string.Empty).Trim().ToLowerInvariant();
        if (kind is not ("legal" or "financial")) throw new ValidationFailedException(Messages.ContractReviewKindInvalid);
        if (decision is not ("approve" or "return")) throw new ValidationFailedException(Messages.ContractReviewDecisionInvalid);

        var c = await LoadAsync(contractId, ct, track: true);
        if (c.Stage != S.Review) throw new ConflictException(Messages.ContractReviewOnlyInReview);
        await _permissions.AuthorizeAnyAsync(Record, c.Id, new[] { kind == "legal" ? ScreenAction.LegalReview : ScreenAction.FinancialReview }, ct);

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var approvals = await RoundApprovalsAsync(c, ct);
        if (decision == "approve" && Approved(approvals, kind))
            throw new ConflictException(string.Format(Messages.ContractAlreadyReviewed, kind == "legal" ? Messages.ContractReviewLegal : Messages.ContractReviewFinancial));

        var record = new ContractApproval
        {
            ContractId = c.Id, Round = c.ReviewRound, Kind = kind, Decision = decision == "approve" ? "approved" : "returned",
            UserId = _user.UserId, UserName = _user.Name ?? "system", Note = note, DecidedAt = DateTime.UtcNow,
        };

        await new TransactionRunner(_db).RunAsync(async token =>
        {
            if (decision == "return")
            {
                // الإعادة للمسودة انتقال عادي: يتطلب الملاحظة ويسجَّل في السجل
                await MoveCoreAsync(c, S.Draft, note, token);
                _db.Add(record);
            }
            else
            {
                // تُحفظ الموافقة أولاً (في المعاملة نفسها) ليراها شرط «موافقة الاثنين» في الانتقال
                _db.Add(record);
                await _db.SaveChangesAsync(token);
                approvals.Add(record);
                // اكتمال الموافقتين في الجولة: ينتقل العقد للاعتماد تلقائياً عبر الانتقال نفسه
                if (Approved(approvals, "legal") && Approved(approvals, "financial"))
                    await MoveCoreAsync(c, S.Approval, note, token);
            }
            await _db.SaveChangesAsync(token);
            return true;
        }, ct);

        if (c.Stage != S.Review) await NotifyStageChangedAsync(c, note, ct); // أُعيد للمسودة أو اكتملت الموافقتان
        else await NotifyCreatorAsync(c, "contract_review_approved",
            string.Format(Messages.ContractNotifyReviewApprovedTitle, kind == "legal" ? Messages.ContractReviewLegal : Messages.ContractReviewFinancial, c.ContractNumber),
            note, ct);
        return await GetAsync(contractId, ct);
    }

    // ---------- الإشعارات ----------

    /// <summary>مراحل تنتظر قراراً: يُشعر فيها كل من يملك إجراءً على انتقالاتها (والمراجعتين في «المراجعة»).</summary>
    private static readonly HashSet<string> PendingStages = new() { S.Review, S.Approval };

    /// <summary>
    /// بعد الانتقال: من يملك التصرف في المرحلة الجديدة («بانتظار إجرائك»)، ومنشئ العقد بالحالة الجديدة. المنفّذ لا يُشعَر نفسه،
    /// وتعذّر الإشعار لا يُسقط الانتقال المحفوظ.
    /// </summary>
    private async Task NotifyStageChangedAsync(CommercialContract c, string? note, CancellationToken ct)
    {
        try
        {
            var me = _user.UserId;
            var stageName = await StatusNameAsync(c.Stage, ct);
            var actors = new List<Guid>();
            if (PendingStages.Contains(c.Stage))
            {
                var actions = (await TransitionsFromAsync(c, ct)).SelectMany(ActionsOf).ToHashSet();
                if (c.Stage == S.Review) { actions.Add(ScreenAction.LegalReview); actions.Add(ScreenAction.FinancialReview); }
                actors = (await _permissions.UsersWithAnyActionAsync(Screen, actions, ct)).Where(id => id != me).ToList();
                if (actors.Count > 0)
                    await _notifications.NotifyUsersAsync(actors, Notice(c, "contract_action_required",
                        string.Format(Messages.ContractNotifyActionTitle, c.ContractNumber, stageName), note), ct);
            }

            if (c.CreatedByUserId is { } creator && !actors.Contains(creator))
            {
                var returned = c.Stage == S.Draft;
                await NotifyCreatorAsync(c, returned ? "contract_returned" : "contract_status_changed",
                    returned ? string.Format(Messages.ContractNotifyReturnedTitle, c.ContractNumber) : string.Format(Messages.ContractNotifyStatusTitle, c.ContractNumber, stageName),
                    note, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "تعذّر إرسال إشعارات انتقال العقد {ContractNumber}.", c.ContractNumber);
        }
    }

    private async Task NotifyCreatorAsync(CommercialContract c, string type, string title, string? note, CancellationToken ct)
    {
        if (c.CreatedByUserId is not { } creator || creator == _user.UserId) return;
        try
        {
            await _notifications.NotifyUsersAsync(new[] { creator }, Notice(c, type, title, note), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "تعذّر إشعار منشئ العقد {ContractNumber}.", c.ContractNumber);
        }
    }

    private NotifyRequestDto Notice(CommercialContract c, string type, string title, string? note) => new()
    {
        Type = type, Title = title,
        Body = string.Format(Messages.ContractNotifyBody, c.Title, c.PartyName, _user.Name ?? "system") + (note == null ? string.Empty : $" — {note}"),
        RelatedDocType = "commercial_contract", RelatedDocId = c.Id, RelatedDocNumber = c.ContractNumber,
    };

    // ---------- التوقيع والضمان ----------

    public async Task<ContractWorkflowDto> SaveSigningAsync(Guid contractId, ContractSigningDto r, CancellationToken ct = default)
    {
        var c = await LoadAsync(contractId, ct, track: true);
        if (c.Stage is not (S.Draft or S.Review or S.Approval)) throw new ConflictException(Messages.ContractSigningClosed);
        await _permissions.AuthorizeAnyAsync(Record, c.Id, new[] { ScreenAction.Approve, ScreenAction.Edit }, ct);
        if (r.GuaranteeAmount < 0) throw new ValidationFailedException(Messages.ContractMissingGuarantee);

        c.SignedDate = r.SignedDate?.Date;
        c.SignedByCompany = Clean(r.SignedByCompany);
        c.SignedByParty = Clean(r.SignedByParty);
        c.GuaranteeNumber = Clean(r.GuaranteeNumber);
        c.GuaranteeBank = Clean(r.GuaranteeBank);
        c.GuaranteeAmount = r.GuaranteeAmount;
        c.GuaranteeExpiry = r.GuaranteeExpiry?.Date;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(contractId, ct);
    }

    // ---------- المرفقات ----------

    public async Task<ContractAttachmentDto> AddAttachmentAsync(Guid contractId, string kind, string fileName, byte[] content, CancellationToken ct = default)
    {
        kind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (!AttachmentKinds.Contains(kind)) throw new ValidationFailedException(Messages.ContractAttachmentKindInvalid);
        var c = await LoadAsync(contractId, ct, track: false);
        await _permissions.AuthorizeAnyAsync(Record, c.Id, new[] { ScreenAction.Edit, ScreenAction.Approve }, ct);

        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length == 0 || content.Length == 0) throw new ValidationFailedException(Messages.LeaveAttachmentEmpty);
        if (content.Length > LeaveAttachmentService.MaxBytes)
            throw new ValidationFailedException(string.Format(Messages.LeaveAttachmentTooLarge, LeaveAttachmentService.MaxBytes / (1024 * 1024)));
        var contentType = LeaveAttachmentService.ContentTypeOf(name, content) ?? throw new ValidationFailedException(Messages.LeaveAttachmentTypeNotAllowed);
        if (await _db.Set<ContractAttachment>().CountAsync(a => a.ContractId == c.Id, ct) >= MaxAttachments)
            throw new ValidationFailedException(string.Format(Messages.LeaveAttachmentLimitReached, MaxAttachments));

        var attachment = new ContractAttachment
        {
            ContractId = c.Id, Kind = kind, FileName = name.Length > 150 ? name[^150..] : name, ContentType = contentType,
            Size = content.Length, Content = content, UploadedBy = _user.Name,
        };
        _db.Add(attachment);
        await _db.SaveChangesAsync(ct);
        return new ContractAttachmentDto
        {
            Id = attachment.Id, Kind = kind, FileName = attachment.FileName, ContentType = contentType, Size = attachment.Size,
            UploadedBy = attachment.UploadedBy, CreatedAt = attachment.CreatedAt,
        };
    }

    public async Task<ContractFileDto> GetAttachmentAsync(Guid contractId, Guid attachmentId, CancellationToken ct = default)
    {
        await _permissions.EnsureRecordInScopeAsync(Record, contractId, ct);
        var a = await _db.Set<ContractAttachment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId && x.ContractId == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractAttachmentNotFound);
        return new ContractFileDto(a.FileName, a.ContentType, a.Content);
    }

    public async Task DeleteAttachmentAsync(Guid contractId, Guid attachmentId, CancellationToken ct = default)
    {
        var c = await LoadAsync(contractId, ct, track: false);
        // مرفقات العقد المفعّل جزء من سجله (النسخة الموقعة والضمان): لا تُحذف بعد التفعيل
        if (c.Stage is not (S.Draft or S.Review or S.Approval)) throw new ConflictException(Messages.ContractSigningClosed);
        await _permissions.AuthorizeAnyAsync(Record, c.Id, new[] { ScreenAction.Edit, ScreenAction.Approve }, ct);
        var a = await _db.Set<ContractAttachment>().FirstOrDefaultAsync(x => x.Id == attachmentId && x.ContractId == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractAttachmentNotFound);
        _db.Remove(a);
        await _db.SaveChangesAsync(ct);
    }

    // ---------- الشروط ----------

    /// <summary>ما ينقص قبل الانتقال (فارغ = جاهز). كل بند جملة واضحة تقول ماذا ينقص بالضبط.</summary>
    private async Task<List<string>> BlockersAsync(CommercialContract c, ContractTransition t, ContractTypeDefinition? type,
        List<ContractApproval> approvals, CancellationToken ct)
    {
        var missing = new List<string>();
        switch (t.Condition)
        {
            case "complete_data":
                if (string.IsNullOrWhiteSpace(c.Title)) missing.Add(Messages.ContractMissingTitle);
                if (c.PartyId == null || string.IsNullOrWhiteSpace(c.PartyName)) missing.Add(Messages.ContractMissingParty);
                if (c.StartDate == null || c.EndDate == null) missing.Add(Messages.ContractMissingDates);
                if (c.ContractValue <= 0) missing.Add(Messages.ContractMissingValue);
                // الفوترة بعد الاستلام تُحسب على البنود؛ المباشرة تكفيها البنود أو المستخلصات
                if (type?.Billing == ContractBilling.Direct)
                {
                    if (c.Items.Count == 0 && c.Milestones.Count == 0) missing.Add(Messages.ContractMissingItemsOrMilestones);
                }
                else if (c.Items.Count == 0) missing.Add(Messages.ContractMissingItems);
                break;

            case "both_reviews":
                if (!Approved(approvals, "legal")) missing.Add(Messages.ContractMissingLegalApproval);
                if (!Approved(approvals, "financial")) missing.Add(Messages.ContractMissingFinancialApproval);
                break;

            case "signed_guarantee_limit":
                // حد الاعتماد يُفحص مع الصلاحية (403)؛ هنا النسخة الموقعة والضمان
                if (!await _db.Set<ContractAttachment>().AnyAsync(a => a.ContractId == c.Id && a.Kind == "signed_copy", ct))
                    missing.Add(Messages.ContractMissingSignedCopy);
                if (type?.RequiresGuarantee == true && !c.IsSimplified)
                {
                    if (string.IsNullOrWhiteSpace(c.GuaranteeNumber) || c.GuaranteeAmount is not > 0 || c.GuaranteeExpiry == null)
                        missing.Add(Messages.ContractMissingGuarantee);
                    else if (c.GuaranteeExpiry.Value.Date < SaudiTime.Today)
                        missing.Add(Messages.ContractGuaranteeExpired);
                }
                break;

            case "all_received":
                if (type?.Billing == ContractBilling.AfterReceipt)
                {
                    var remaining = await RemainingQuantitiesAsync(c, ct);
                    if (remaining.Count > 0) missing.Add(string.Format(Messages.ContractMissingReceipts, string.Join("، ", remaining)));
                }
                break;

            case "all_billed_retention_released":
                if (type?.Billing == ContractBilling.AfterReceipt)
                {
                    var unbilled = await _db.Set<DeliveryNote>().AsNoTracking()
                        .Where(n => n.ContractId == c.Id && n.Status != DeliveryNoteStatus.Draft && n.Status != DeliveryNoteStatus.Returned && n.InvoiceId == null)
                        .Select(n => n.DeliveryNumber).ToListAsync(ct);
                    if (unbilled.Count > 0) missing.Add(string.Format(Messages.ContractMissingInvoicing, string.Join("، ", unbilled)));
                }
                else
                {
                    var open = c.Milestones.Where(m => m.Status is ContractMilestoneStatus.Pending or ContractMilestoneStatus.Due)
                        .OrderBy(m => m.MilestoneNumber).Select(m => m.MilestoneNumber.ToString()).ToList();
                    if (open.Count > 0) missing.Add(string.Format(Messages.ContractMissingMilestones, string.Join("، ", open)));
                }
                if (c.RetentionAmount > 0 && !await _db.Set<ContractInvoice>().AnyAsync(i => i.ContractId == c.Id && i.Kind == "retention_release", ct))
                    missing.Add(Messages.ContractMissingRetentionRelease);
                break;

            case null or "":
                break;

            default:
                // شرط مكتوب في الجدول ولا يعرفه الكود: يُغلق الانتقال بدل أن يمر بلا فحص
                missing.Add(string.Format(Messages.ContractUnknownCondition, t.Condition));
                break;
        }
        return missing;
    }

    /// <summary>بنود العقد التي لم تُستلم كميتها كاملة (صافي الاستلام بعد المرتجع)، بصيغة «الصنف: المتبقي».</summary>
    private async Task<List<string>> RemainingQuantitiesAsync(CommercialContract c, CancellationToken ct)
    {
        var received = await _db.Set<DeliveryNote>().AsNoTracking()
            .Where(n => n.ContractId == c.Id && n.Status != DeliveryNoteStatus.Draft)
            .SelectMany(n => n.Items)
            .GroupBy(i => i.ItemId)
            .Select(g => new { ItemId = g.Key, Net = g.Sum(i => i.DeliveredQty - i.ReturnedQty) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Net, ct);
        return c.Items.Where(i => i.ItemId.HasValue)
            .GroupBy(i => i.ItemId!.Value)
            .Select(g => new { Name = g.First().Description, Remaining = g.Sum(i => i.Quantity) - received.GetValueOrDefault(g.Key) })
            .Where(x => x.Remaining > 0)
            .Select(x => $"{x.Name}: {x.Remaining:0.###}")
            .ToList();
    }

    // ---------- مساعدات ----------

    private async Task<CommercialContract> LoadAsync(Guid id, CancellationToken ct, bool track)
    {
        var q = _db.Set<CommercialContract>().Include(x => x.Items).Include(x => x.Milestones).AsSplitQuery();
        return await (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
    }

    private Task<ContractTypeDefinition?> TypeAsync(string code, CancellationToken ct)
        => _db.Set<ContractTypeDefinition>().AsNoTracking().FirstOrDefaultAsync(t => t.Code == code, ct);

    /// <summary>الانتقالات المعرّفة من حالة العقد لمساره (كامل/مبسط)؛ ما سواها غير موجود بالنسبة للعقد.</summary>
    private Task<List<ContractTransition>> TransitionsFromAsync(CommercialContract c, CancellationToken ct)
    {
        var workflow = c.IsSimplified ? ContractWorkflows.Simple : ContractWorkflows.Full;
        return _db.Set<ContractTransition>().AsNoTracking()
            .Where(t => t.IsActive && t.FromStatus == c.Stage && (t.Workflow == null || t.Workflow == "" || t.Workflow == workflow))
            .OrderBy(t => t.SortOrder).ToListAsync(ct);
    }

    private Task<List<ContractApproval>> RoundApprovalsAsync(CommercialContract c, CancellationToken ct)
        => _db.Set<ContractApproval>().AsNoTracking().Where(a => a.ContractId == c.Id && a.Round == c.ReviewRound).OrderBy(a => a.DecidedAt).ToListAsync(ct);

    private static bool Approved(IEnumerable<ContractApproval> approvals, string kind)
        => approvals.Any(a => a.Kind == kind && a.Decision == "approved");

    private static ScreenAction[] ActionsOf(ContractTransition t) => PermissionStore.ParseActions(t.RequiredActions).ToArray();

    private async Task<bool> CanAnyAsync(ContractTransition t)
    {
        foreach (var a in ActionsOf(t))
            if (await _permissions.HasPermissionAsync(Screen, a)) return true;
        return false;
    }

    private async Task<string> StatusNameAsync(string status, CancellationToken ct)
        => await _db.Set<S>().AsNoTracking().Where(s => s.Id == status).Select(s => s.NameAr).FirstOrDefaultAsync(ct) ?? status;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
