using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IAccountService : ICrudService<AccountDto, CreateAccountDto, UpdateAccountDto>
{
    /// <summary>شجرة الحسابات كاملة (مع الأبناء) لعرضها في الواجهة.</summary>
    Task<List<AccountDto>> GetTreeAsync(CancellationToken ct = default);
    Task<AccountDto> GetByCodeAsync(string code, CancellationToken ct = default);
    /// <summary>ينشئ حساباً فرعياً تلقائياً لعميل/مورد/بنك تحت الحساب الأب (112 / 211 / 111) ويُرجع كوده. آمن للتكرار.</summary>
    Task<string> EnsureLinkedAccountAsync(LinkedEntityType type, Guid entityId, string nameAr, string nameEn, CancellationToken ct = default);
    /// <summary>
    /// مثل السابق، و<paramref name="requestedCode"/> (غير فارغ) رمز اختاره المستخدم: يُحفظ كما هو بعد التحقق (أرقام، غير مستخدم،
    /// تحت مجموعة الكيان أو أحد فروعها)، وأبوه أطول حساب قائم رمزه بادئة له.
    /// </summary>
    Task<string> EnsureLinkedAccountAsync(LinkedEntityType type, Guid entityId, string nameAr, string nameEn, string? requestedCode, CancellationToken ct = default);
    /// <summary>
    /// ينشئ حساباً فرعياً مرتبطاً بكيان تحت الحساب الأب <paramref name="parentCode"/> (يرث نوعه وطبيعته وعملته وسياسة مراكز التكلفة)
    /// برقم مركزي لا يعيد أرقام المحذوف، ويُرجع معرّفه ورمزه. يُستدعى داخل معاملة الكيان.
    /// </summary>
    /// <summary>
    /// هل يمكن إضافة حساب فرعي تحت الحساب دون كسر ما يرحّل عليه؟ نعم إن كان تجميعياً (له فروع)، أو بلا استخدام مباشر:
    /// لا قيود عليه، ولا أصل ثابت يشير إليه، ولا إعداد حسابات أو طريقة دفع تستخدمه (المحرك يرفض الترحيل على حساب أب).
    /// </summary>
    Task<bool> CanHoldSubAccountsAsync(string code, CancellationToken ct = default);
    Task<(Guid Id, string Code)> CreateLinkedChildAccountAsync(string parentCode, LinkedEntityType type, Guid entityId, string nameAr, string nameEn, CancellationToken ct = default);
}
