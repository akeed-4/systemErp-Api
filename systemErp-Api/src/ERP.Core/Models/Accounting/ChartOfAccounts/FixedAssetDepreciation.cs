
namespace ERP.Core.Models.Accounting;

/// <summary>
/// إهلاك مرحَّل لأصل عن فترة شهرية واحدة. وجود السجل يعني أن الفترة رُحِّلت، وفهرس فريد (المنشأة + الأصل + الفترة)
/// يمنع ترحيل نفس الأصل مرتين لنفس الفترة. مركز التكلفة يُحفظ كما كان وقت الترحيل.
/// </summary>
public class FixedAssetDepreciation : BaseEntity
{
    public Guid FixedAssetId { get; set; }
    /// <summary>الفترة بصيغة yyyy-MM.</summary>
    public string Period { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid CostCenterId { get; set; }
    public Guid JournalEntryId { get; set; }
    public string JournalEntryNumber { get; set; } = string.Empty;
    public DateTime PostedAt { get; set; }
    public Guid? PostedByUserId { get; set; }
    public string? PostedBy { get; set; }
}
