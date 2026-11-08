using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// المناقصات (مسودات عقود المقاولات قبل الترسية): السجل بالمواعيد والقيم، ومسار الحالة، ونسخ العروض المقدَّمة.
/// الصلاحيات ونطاق سجل العقد يفحصها المتحكم.
/// </summary>
public interface IContractTenderService
{
    /// <summary>كل العقود ذات المناقصة (في نطاق الوصول)، الأقرب موعداً أولاً للمفتوحة.</summary>
    Task<List<TenderRowDto>> ListAsync(CancellationToken ct = default);
    Task<TenderRowDto> GetAsync(Guid contractId, CancellationToken ct = default);
    Task<List<TenderRevisionDto>> RevisionsAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>
    /// نقل الحالة: التقديم يحفظ نسخة من العرض، والترسية تنشئ ملف المشروع ومركز تكلفته، والخسارة بسبب. في مسودة العقد وحدها.
    /// </summary>
    Task<TenderRowDto> MoveAsync(Guid contractId, MoveTenderDto request, CancellationToken ct = default);
}
