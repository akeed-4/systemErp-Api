using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>يُنتج أرقام المستندات المتسلسلة لكل منشأة (فاتورة، قيد، سند، ...).</summary>
public interface INumberSequenceService
{
    /// <summary>الرقم التالي، مثال: SINV-000001. يجب استدعاؤه داخل نفس معاملة المستند.</summary>
    Task<string> NextAsync(string key, string defaultPrefix, CancellationToken ct = default);
}
