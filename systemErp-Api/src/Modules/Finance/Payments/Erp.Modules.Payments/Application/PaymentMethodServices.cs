using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Banking.Contracts;
using Erp.Modules.Payments.Contracts;
using Erp.Modules.Payments.Domain;
using Erp.Modules.Payments.Persistence;
using Erp.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Payments.Application;

/// <summary>The frontend's default payment methods (erp.service.ts), linked to accounts through the posting mappings.</summary>
internal sealed class PaymentsSeeder(PaymentsDbContext db, IAccountLookup accounts) : IModuleSeeder
{
    public int Order => 90;

    public async Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (await db.PaymentMethods.AnyAsync(cancellationToken))
        {
            return;
        }

        var cash = (await accounts.ResolveAsync(PostingPurpose.CashOnHand, cancellationToken)).Id;
        var cards = (await accounts.ResolveAsync(PostingPurpose.CardClearing, cancellationToken)).Id;
        var financing = (await accounts.ResolveAsync(PostingPurpose.FinancingBankReceivable, cancellationToken)).Id;

        (string Code, string Ar, string En, PaymentMethodType Type, PaymentChannel Channel, Guid? Account, string Icon, bool Reference)[] defaults =
        [
            ("CASH", "نقدي (الخزينة)", "Cash (Vault)", PaymentMethodType.Cash, PaymentChannel.Cash, cash, "payments", false),
            ("MADA", "شبكة مدى", "Mada", PaymentMethodType.Card, PaymentChannel.Mada, cards, "credit_card", true),
            ("CARD", "بطاقة ائتمانية (فيزا / ماستركارد)", "Credit Card (Visa / Mastercard)", PaymentMethodType.Card, PaymentChannel.VisaMaster, cards, "credit_card", true),
            ("APPLE_PAY", "Apple Pay", "Apple Pay", PaymentMethodType.Card, PaymentChannel.ApplePay, cards, "phone_iphone", true),
            ("BANK_TRANSFER", "تحويل بنكي مباشر (سريع)", "Bank Transfer (SARIE)", PaymentMethodType.Bank, PaymentChannel.Transfer, null, "account_balance", true),
            ("BANK_LEASE", "تمويل تأجيري بنكي", "Bank Financing / Lease", PaymentMethodType.Bank, PaymentChannel.Financing, financing, "account_balance_wallet", true),
            ("CREDIT", "آجل (ذمم مدينة)", "Credit (On Account)", PaymentMethodType.Credit, PaymentChannel.Credit, null, "assignment_ind", false),
        ];

        foreach (var d in defaults)
        {
            var method = new PaymentMethod(d.Code);
            method.Update(d.Ar, d.En, d.Type, d.Channel, d.Account, null, d.Icon, 0, d.Reference, isActive: true);
            db.PaymentMethods.Add(method);
        }
    }
}

internal sealed class PaymentMethodDirectory(PaymentsDbContext db, IBankDirectory banks) : IPaymentMethodDirectory
{
    public async Task<PaymentMethodSummary?> FindAsync(Guid paymentMethodId, CancellationToken cancellationToken) =>
        (await FindManyAsync([paymentMethodId], cancellationToken)).GetValueOrDefault(paymentMethodId);

    public async Task<IReadOnlyDictionary<Guid, PaymentMethodSummary>> FindManyAsync(IReadOnlyCollection<Guid> paymentMethodIds, CancellationToken cancellationToken) =>
        await db.PaymentMethods.AsNoTracking()
            .Where(m => paymentMethodIds.Contains(m.Id))
            .Select(m => new PaymentMethodSummary(m.Id, m.Code, m.NameAr, m.NameEn, m.Type, m.Channel, m.AccountId, m.BankAccountId, m.CommissionPercent, m.RequiresReference, m.IsActive))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

    public async Task<Guid> ResolveTreasuryAccountAsync(Guid paymentMethodId, Guid? bankAccountId, CancellationToken cancellationToken)
    {
        var method = await FindAsync(paymentMethodId, cancellationToken);
        if (method is null || !method.IsActive)
        {
            throw ErpException.Validation("Unknown or inactive payment method.", "طريقة الدفع غير موجودة أو غير مفعّلة.");
        }

        if (method.Type == PaymentMethodType.Credit)
        {
            throw ErpException.Validation("'Credit' is a settlement term, not a way to receive or pay money.", "الآجل ليس طريقة لاستلام أو صرف النقد.");
        }

        var bankAccount = bankAccountId ?? method.BankAccountId;
        if (bankAccount is { } id)
        {
            var account = await banks.FindBankAccountAsync(id, cancellationToken);
            return account is { IsActive: true, AccountId: { } glAccount }
                ? glAccount
                : throw ErpException.Validation("Unknown or inactive bank account.", "الحساب البنكي غير موجود أو غير نشط.");
        }

        return method.AccountId ?? throw ErpException.Validation(
            $"Payment method {method.Code} has no linked account; choose a bank account.",
            $"طريقة الدفع {method.NameAr} غير مربوطة بحساب؛ يرجى اختيار الحساب البنكي.");
    }
}
