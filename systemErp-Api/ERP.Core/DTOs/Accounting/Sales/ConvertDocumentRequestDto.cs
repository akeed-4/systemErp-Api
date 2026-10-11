using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class ConvertDocumentRequestDto
{
    /// <summary>invoice | order | purchase_order حسب المستند المصدر.</summary>
    public string? Target { get; set; }
}
