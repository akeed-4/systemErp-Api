using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Payments.Application;
using Erp.Modules.Payments.Contracts;
using Erp.Modules.Payments.Domain;
using Erp.Modules.Payments.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Payments.Endpoints;

/// <summary>The frontend PaymentMethodItem shape.</summary>
internal sealed record PaymentMethodDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    PaymentMethodType Type,
    PaymentChannel Channel,
    Guid? LinkedAccountId,
    string? LinkedAccountCode,
    string? LinkedAccountName,
    Guid? BankAccountId,
    string? Icon,
    decimal CommissionPercent,
    bool RequiresReference,
    string Status);

internal sealed record SavePaymentMethodRequest(
    string? Code,
    string NameAr,
    string? NameEn,
    PaymentMethodType Type,
    PaymentChannel? Channel,
    Guid? LinkedAccountId,
    string? LinkedAccountCode,
    Guid? BankAccountId,
    string? Icon,
    decimal? CommissionPercent,
    bool? RequiresReference,
    string? Status);

internal sealed record VoucherPaymentRequest(Guid PaymentMethodId, decimal Amount, Guid? BankAccountId, string? Reference);

internal sealed record CreateVoucherRequest(
    VoucherType Type,
    DateOnly? Date,
    VoucherPartyType? PartyType,
    Guid? PartyId,
    string? PartyName,
    Guid? PartyAccountId,
    string? PartyAccountCode,
    IReadOnlyList<VoucherPaymentRequest>? Payments,
    Guid? PaymentMethodId,
    decimal? Amount,
    Guid? BankAccountId,
    string? ReferenceNumber,
    string? Notes,
    string? ReceivedOrPaidBy,
    Guid? CostCenterId,
    bool? Post);

internal sealed record CancelVoucherRequest(string? Reason);

/// <summary>payment-methods and vouchers (receipt / payment) + {post, cancel}.</summary>
internal static class PaymentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var methods = app.MapGroup("/api/v1/payment-methods").WithTags("Payments");
        methods.MapGet(string.Empty, ListMethodsAsync).RequireAuthorization();
        methods.MapPost(string.Empty, CreateMethodAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);
        methods.MapPut("{id:guid}", UpdateMethodAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);
        methods.MapDelete("{id:guid}", DeleteMethodAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Delete);

        var vouchers = app.MapGroup("/api/v1/vouchers").WithTags("Payments");
        vouchers.MapGet(string.Empty, async ([AsParameters] PaginationParams paging, VoucherType? type, VoucherService service, CancellationToken ct) =>
            ErpResults.Ok(await service.ListAsync(paging, type, ct))).RequireScreen(ScreenIds.Vouchers, ScreenAction.View);
        vouchers.MapGet("{id:guid}", async (Guid id, VoucherService service, CancellationToken ct) =>
            ErpResults.Ok(await service.GetAsync(id, ct))).RequireScreen(ScreenIds.Vouchers, ScreenAction.View);
        vouchers.MapPost(string.Empty, CreateVoucherAsync).RequireScreen(ScreenIds.Vouchers, ScreenAction.Create);
        vouchers.MapDelete("{id:guid}", async (Guid id, VoucherService service, CancellationToken ct) =>
        {
            await service.DeleteDraftAsync(id, ct);
            return ErpResults.Ok(new { id }, "تم حذف السند");
        }).RequireScreen(ScreenIds.Vouchers, ScreenAction.Delete);
        vouchers.MapPost("{id:guid}/post", async (Guid id, VoucherService service, CancellationToken ct) =>
            ErpResults.Ok(await service.PostDraftAsync(id, ct), "تم ترحيل السند")).RequireScreen(ScreenIds.Vouchers, ScreenAction.Approve);
        vouchers.MapPost("{id:guid}/cancel", async (Guid id, CancelVoucherRequest? request, VoucherService service, CancellationToken ct) =>
            ErpResults.Ok(await service.CancelAsync(id, request?.Reason, ct), "تم إلغاء السند وعكس قيده")).RequireScreen(ScreenIds.Vouchers, ScreenAction.Delete);
    }

    private static async Task<IResult> CreateVoucherAsync(CreateVoucherRequest request, VoucherService service, IAccountLookup accounts, TimeProvider clock, CancellationToken ct)
    {
        var payments = request.Payments is { Count: > 0 }
            ? request.Payments.Select(p => new VoucherPaymentInput(p.PaymentMethodId, p.Amount, p.BankAccountId, p.Reference)).ToList()
            : request.PaymentMethodId is { } methodId && request.Amount is { } amount
                ? [new VoucherPaymentInput(methodId, amount, request.BankAccountId, request.ReferenceNumber)]
                : [];

        var partyAccountId = request.PartyAccountId;
        if (partyAccountId is null && !string.IsNullOrWhiteSpace(request.PartyAccountCode))
        {
            partyAccountId = (await accounts.FindByCodeAsync(request.PartyAccountCode, ct))?.Id
                ?? throw ErpException.Validation($"Unknown account '{request.PartyAccountCode}'.", $"الحساب '{request.PartyAccountCode}' غير موجود.");
        }

        var result = await service.CreateAsync(
            new CreateVoucherCommand(
                request.Type,
                request.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
                request.PartyType ?? VoucherPartyType.Other,
                request.PartyId,
                request.PartyName,
                partyAccountId,
                payments,
                request.ReferenceNumber,
                request.Notes,
                request.ReceivedOrPaidBy,
                request.CostCenterId,
                Allocations: null,
                Post: request.Post ?? true),
            ct);

        return ErpResults.Created($"/api/v1/vouchers/{result.VoucherId}", await service.GetAsync(result.VoucherId, ct), "تم حفظ السند");
    }

    private static async Task<IResult> ListMethodsAsync(PaymentsDbContext db, IAccountLookup accounts, CancellationToken ct)
    {
        var list = await db.PaymentMethods.AsNoTracking().OrderBy(m => m.Code).ToListAsync(ct);
        var linked = await accounts.FindManyAsync(list.Where(m => m.AccountId is not null).Select(m => m.AccountId!.Value).ToList(), ct);
        return ErpResults.Ok(list.Select(m => ToDto(m, m.AccountId is { } a ? linked.GetValueOrDefault(a) : null)).ToList());
    }

    private static async Task<IResult> CreateMethodAsync(SavePaymentMethodRequest request, PaymentsDbContext db, IAccountLookup accounts, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw ErpException.Validation("The code is required.", "الرمز مطلوب.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.PaymentMethods.AnyAsync(m => m.Code == code, ct))
        {
            throw ErpException.Conflict("payment_method_code_taken", $"Payment method {code} already exists.", $"طريقة الدفع {code} موجودة مسبقاً.");
        }

        var method = new PaymentMethod(code);
        var account = await ApplyAsync(method, request, accounts, ct);
        db.PaymentMethods.Add(method);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/payment-methods/{method.Id}", ToDto(method, account), "تم إضافة طريقة الدفع");
    }

    private static async Task<IResult> UpdateMethodAsync(Guid id, SavePaymentMethodRequest request, PaymentsDbContext db, IAccountLookup accounts, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var method = await db.PaymentMethods.SingleOrDefaultAsync(m => m.Id == id, ct) ?? throw ErpException.NotFound("Payment method", "طريقة الدفع");
        var account = await ApplyAsync(method, request, accounts, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(method, account), "تم تحديث طريقة الدفع");
    }

    private static async Task<IResult> DeleteMethodAsync(Guid id, PaymentsDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var method = await db.PaymentMethods.SingleOrDefaultAsync(m => m.Id == id, ct) ?? throw ErpException.NotFound("Payment method", "طريقة الدفع");
        if (await db.VoucherPayments.AnyAsync(p => p.PaymentMethodId == id, ct))
        {
            throw ErpException.Conflict("payment_method_used", "The payment method is used; deactivate it instead.", "طريقة الدفع مستخدمة؛ يمكنك إيقافها بدلاً من حذفها.");
        }

        db.PaymentMethods.Remove(method);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(new { id }, "تم حذف طريقة الدفع");
    }

    private static async Task<AccountSummary?> ApplyAsync(PaymentMethod method, SavePaymentMethodRequest request, IAccountLookup accounts, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr))
        {
            throw ErpException.Validation("The Arabic name is required.", "الاسم بالعربي مطلوب.");
        }

        AccountSummary? account = null;
        if (request.LinkedAccountId is { } accountId)
        {
            account = await accounts.FindAsync(accountId, ct);
        }
        else if (!string.IsNullOrWhiteSpace(request.LinkedAccountCode))
        {
            account = await accounts.FindByCodeAsync(request.LinkedAccountCode, ct);
        }

        if ((request.LinkedAccountId is not null || !string.IsNullOrWhiteSpace(request.LinkedAccountCode)) && account is not { IsPostable: true, IsActive: true })
        {
            throw ErpException.Validation("The linked account must be an active sub-account.", "الحساب المربوط يجب أن يكون حساباً فرعياً نشطاً.");
        }

        var channel = request.Channel ?? request.Type switch
        {
            PaymentMethodType.Cash => PaymentChannel.Cash,
            PaymentMethodType.Card => PaymentChannel.Mada,
            PaymentMethodType.Bank => PaymentChannel.Transfer,
            PaymentMethodType.Cheque => PaymentChannel.Cheque,
            _ => PaymentChannel.Credit,
        };

        method.Update(
            request.NameAr,
            request.NameEn ?? string.Empty,
            request.Type,
            channel,
            account?.Id,
            request.BankAccountId,
            request.Icon,
            request.CommissionPercent ?? 0,
            request.RequiresReference ?? false,
            !string.Equals(request.Status, "inactive", StringComparison.OrdinalIgnoreCase));
        return account;
    }

    private static PaymentMethodDto ToDto(PaymentMethod m, AccountSummary? account) =>
        new(m.Id, m.Code, m.NameAr, m.NameEn, m.Type, m.Channel, m.AccountId, account?.Code, account?.NameAr, m.BankAccountId, m.Icon, m.CommissionPercent,
            m.RequiresReference, m.IsActive ? "active" : "inactive");
}
