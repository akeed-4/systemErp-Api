using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

public interface IAccountingSettingsService
{
    Task<AccountingSettingsDto> GetAsync(CancellationToken ct = default);
    Task<AccountingSettingsDto> SaveAsync(SaveAccountingSettingsDto settings, CancellationToken ct = default);
    /// <summary>ينشئ حسابات العقود الناقصة (أو يربط القائم المناسب) ويربطها بالإعدادات غير المحددة؛ لا يغيّر اختيار المستخدم.</summary>
    Task<ContractAccountsSetupDto> InitializeContractAccountsAsync(CancellationToken ct = default);
    /// <summary>فحص اكتمال حسابات الجرد الدوري بلا كتابة.</summary>
    Task<ContractAccountsSetupDto> GetPeriodicAccountsStatusAsync(CancellationToken ct = default);
    /// <summary>ينشئ حسابات الجرد الدوري الناقصة (أو يربط القائم المناسب)؛ لا يغيّر اختيار المستخدم ولا ينشئ حساب مخزون.</summary>
    Task<ContractAccountsSetupDto> InitializePeriodicAccountsAsync(CancellationToken ct = default);
    /// <summary>فحص اكتمال حسابات إضافة (<c>workshop</c> أو <c>equipment_rental</c>) بلا كتابة.</summary>
    Task<ContractAccountsSetupDto> GetModuleAccountsStatusAsync(string module, CancellationToken ct = default);
    /// <summary>ينشئ حسابات الإضافة الناقصة (أو يربط القائم المناسب)؛ لا يغيّر اختيار المستخدم.</summary>
    Task<ContractAccountsSetupDto> InitializeModuleAccountsAsync(string module, CancellationToken ct = default);
    /// <summary>معاينة معالجة قيود الجرد الدوري القديمة للمنشأة الحالية (بلا كتابة).</summary>
    Task<PeriodicLegacyPreviewDto> GetPeriodicLegacyAsync(CancellationToken ct = default);
    /// <summary>تنفيذ المعالجة: قيد إعادة تصنيف لكل قيد قديم جاهز (بصمة المعاينة إلزامية).</summary>
    Task<PeriodicLegacyResultDto> ApplyPeriodicLegacyAsync(ApplyPeriodicLegacyDto request, CancellationToken ct = default);
}
