using System.Security.Cryptography;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

/// <summary>
/// دورة دفع جهاز الدفع: Pending ← (Approved | Declined | NotSent | Unknown)، والمجهول يُحسم بالاستعلام من الجهاز أو إدارياً.
/// لا يُفترض هنا أي نجاح: الموافقة لا تُقبل إلا برقم مرجع العملية من الجهاز (RRN).
/// </summary>
public class CardTerminalPaymentService : ICardTerminalPaymentService
{
    /// <summary>مزوّدو الربط المعروفون (يُضاف المزوّد هنا عند اعتماد بروتوكوله).</summary>
    public static readonly string[] Providers = { "geidea_webecr" };

    private static readonly CardTerminalPaymentStatus[] Open = { CardTerminalPaymentStatus.Pending, CardTerminalPaymentStatus.Unknown };

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ITransactionRunner _tx;
    private readonly IPermissionService _permissions;

    public CardTerminalPaymentService(ErpDbContext db, ICurrentUser user, ITransactionRunner tx, IPermissionService permissions)
    {
        _db = db; _user = user; _tx = tx; _permissions = permissions;
    }

    public Task<CardTerminalPaymentDto> StartAsync(StartCardTerminalPaymentDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            if (!Providers.Contains(r.Provider)) throw new ValidationFailedException(Messages.CardTerminalUnknownProvider);
            var amount = DocumentPricing.Round(r.Amount);
            if (amount <= 0) throw new ValidationFailedException(Messages.AmountMustBePositive);
            var method = await PaymentMethodRules.RequireAsync(_db, r.PaymentMethodCode, token) ?? throw new ValidationFailedException(Messages.InvalidPaymentMethod);
            if (!method.UseCardTerminal) throw new ValidationFailedException(Messages.CardTerminalMethodNotEnabled);
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token)
                ?? throw new ConflictException(Messages.OpenShiftBeforeSale);
            // لا دفع جديد ما دام دفع سابق لم تُحسم نتيجته: قد يكون خُصم، وإعادة المحاولة تخصم مرتين
            if (await _db.Set<CardTerminalPayment>().AnyAsync(p => p.ShiftId == shift.Id && Open.Contains(p.Status), token))
                throw new ConflictException(Messages.CardTerminalPaymentUnresolved);

            var payment = new CardTerminalPayment
            {
                Provider = r.Provider, Amount = amount, ShiftId = shift.Id, CashierId = me,
                TerminalName = string.IsNullOrWhiteSpace(r.TerminalName) ? shift.PosTerminalName : r.TerminalName.Trim(),
                PaymentMethodCode = method.Code, Reference = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            };
            _db.Add(payment);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<CardTerminalPaymentDto>(payment);
        }, ct);

    public Task<CardTerminalPaymentDto> ReportAsync(Guid id, ReportCardTerminalResultDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            var p = await FindAsync(id, token);
            if (p.CashierId != me) throw new ForbiddenException(Messages.CardTerminalPaymentOtherCashier);
            if (!Open.Contains(p.Status)) throw new ConflictException(Messages.CardTerminalPaymentFinal);

            var outcome = (r.Outcome ?? string.Empty).Trim().ToLowerInvariant();
            switch (outcome)
            {
                case "approved":
                    if (string.IsNullOrWhiteSpace(r.Rrn)) throw new ValidationFailedException(Messages.CardTerminalApprovalNeedsRrn);
                    p.Status = CardTerminalPaymentStatus.Approved; break;
                case "declined": p.Status = CardTerminalPaymentStatus.Declined; break;
                // «لم يُرسل» لا يصح بعد أن صارت النتيجة مجهولة: الطلب وصل الجهاز في محاولة سابقة
                case "not_sent" when p.Status == CardTerminalPaymentStatus.Pending: p.Status = CardTerminalPaymentStatus.NotSent; break;
                case "unknown": p.Status = CardTerminalPaymentStatus.Unknown; break;
                default: throw new ValidationFailedException(Messages.CardTerminalOutcomeValues);
            }
            p.Rrn = Clean(r.Rrn) ?? p.Rrn; p.AuthCode = Clean(r.AuthCode) ?? p.AuthCode; p.CardScheme = Clean(r.CardScheme) ?? p.CardScheme;
            p.MaskedPan = Clean(r.MaskedPan) ?? p.MaskedPan; p.TerminalId = Clean(r.TerminalId) ?? p.TerminalId;
            p.ResponseCode = Clean(r.ResponseCode) ?? p.ResponseCode; p.ResponseMessage = Clean(r.ResponseMessage) ?? p.ResponseMessage;
            p.RawResponse = string.IsNullOrWhiteSpace(r.RawResponse) ? p.RawResponse : r.RawResponse;
            if (p.Status != CardTerminalPaymentStatus.Unknown) p.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token);
            return Mapper.Map<CardTerminalPaymentDto>(p);
        }, ct);

    public Task<CardTerminalPaymentDto> ResolveAsync(Guid id, ResolveCardTerminalPaymentDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (!await _permissions.HasPermissionAsync("sales", ScreenAction.Approve, token))
                throw new ForbiddenException(Messages.CardTerminalResolveRequiresApproval);
            if (string.IsNullOrWhiteSpace(r.Note)) throw new ValidationFailedException(Messages.CardTerminalResolveNoteRequired);
            var p = await FindAsync(id, token);

            var outcome = (r.Outcome ?? string.Empty).Trim().ToLowerInvariant();
            switch (outcome)
            {
                case "approved" when Open.Contains(p.Status):
                    if (string.IsNullOrWhiteSpace(r.Rrn) || string.IsNullOrWhiteSpace(r.AuthCode))
                        throw new ValidationFailedException(Messages.CardTerminalManualApprovalNeedsRefs);
                    p.Status = CardTerminalPaymentStatus.Approved; p.Rrn = r.Rrn.Trim(); p.AuthCode = r.AuthCode.Trim(); break;
                case "declined" when Open.Contains(p.Status): p.Status = CardTerminalPaymentStatus.Declined; break;
                case "reversed" when p.Status == CardTerminalPaymentStatus.Approved && p.ConsumedAt == null:
                    p.Status = CardTerminalPaymentStatus.Reversed; break;
                case "approved" or "declined" or "reversed": throw new ConflictException(Messages.CardTerminalPaymentFinal);
                default: throw new ValidationFailedException(Messages.CardTerminalResolveOutcomeValues);
            }
            p.ResolvedManually = true;
            p.ResolvedBy = _user.Name;
            p.ResolutionNote = r.Note.Trim();
            p.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token);
            return Mapper.Map<CardTerminalPaymentDto>(p);
        }, ct);

    public async Task<CardTerminalPaymentDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<CardTerminalPaymentDto>(await FindAsync(id, ct));

    public async Task<List<CardTerminalPaymentDto>> ListAsync(Guid? shiftId, bool unresolvedOnly, CancellationToken ct = default)
    {
        if (!shiftId.HasValue)
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            shiftId = await _db.Set<PosShift>().Where(s => s.CashierId == me && s.Status == PosShiftStatus.Open).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (!shiftId.HasValue) return new();
        }
        var q = _db.Set<CardTerminalPayment>().AsNoTracking().Where(p => p.ShiftId == shiftId);
        if (unresolvedOnly) q = Unresolved(q);
        return (await q.OrderByDescending(p => p.CreatedAt).ToListAsync(ct)).Select(Mapper.Map<CardTerminalPaymentDto>).ToList();
    }

    /// <summary>ما يمنع إغلاق الوردية: معلّق أو مجهول (قد يكون خُصم)، أو موافقة لم تُستخدم في بيع (خُصم بلا فاتورة).</summary>
    internal static IQueryable<CardTerminalPayment> Unresolved(IQueryable<CardTerminalPayment> q)
        => q.Where(p => Open.Contains(p.Status) || (p.Status == CardTerminalPaymentStatus.Approved && p.ConsumedAt == null));

    private async Task<CardTerminalPayment> FindAsync(Guid id, CancellationToken ct)
        => await _db.Set<CardTerminalPayment>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException(Messages.CardTerminalPaymentNotFound);

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
