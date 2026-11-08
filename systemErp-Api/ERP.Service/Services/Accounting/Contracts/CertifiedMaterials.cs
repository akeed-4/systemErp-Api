using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// كميات أذونات تسليم المواد المختارة في المستخلصات: المصدر الواحد لحساب «المختار سابقاً» في الإذن والمستخلص والمتابعة.
/// تُحسب من المستخلصات غير الملغاة وحدها، فإلغاء المستخلص (أو إلغاء فاتورته، الذي يلغيه) يعيد كمياتها متاحة دون أي عدّاد مخزَّن.
/// </summary>
internal static class CertifiedMaterials
{
    public static IQueryable<ContractCertificateMaterial> Active(ErpDbContext db, Guid contractId, Guid? exceptCertificateId = null)
        => db.Set<ContractCertificateMaterial>().AsNoTracking()
            .Where(m => m.Certificate.ContractId == contractId && m.Certificate.Status != ContractCertificateStatuses.Cancelled
                && (exceptCertificateId == null || m.CertificateId != exceptCertificateId));

    /// <summary>
    /// المسحوب من كل سطر تسليم بالإرجاع للمستودع والهدر في الموقع (مستندات مرحّلة، عدا مستند بعينه عند تعديله): يُطرح من المتاح
    /// للمستخلص وللإرجاع، فلا تُعترف تكلفة الكمية نفسها مرتين.
    /// </summary>
    public static async Task<Dictionary<Guid, (decimal Quantity, decimal Cost)>> WithdrawnByLineAsync(ErpDbContext db, Guid contractId,
        Guid? exceptIssueId, CancellationToken ct)
        => (await db.Set<ProjectMaterialIssueLine>().AsNoTracking()
                .Where(l => l.Issue.ContractId == contractId && l.Issue.Status == ProjectMaterialIssueStatuses.Posted
                    && l.Issue.Kind != ProjectMaterialIssueKinds.Issue && l.SourceLineId != null && (exceptIssueId == null || l.IssueId != exceptIssueId))
                .GroupBy(l => l.SourceLineId!.Value)
                .Select(g => new { g.Key, Q = g.Sum(l => l.Quantity), C = g.Sum(l => l.TotalCost) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Q, x.C));

    /// <summary>المختار من كل سطر إذن (كمية وتكلفة) في المستخلصات غير الملغاة، عدا مستخلص بعينه عند تعديله.</summary>
    public static async Task<Dictionary<Guid, (decimal Quantity, decimal Cost)>> ByIssueLineAsync(ErpDbContext db, Guid contractId,
        Guid? exceptCertificateId, CancellationToken ct)
        => (await Active(db, contractId, exceptCertificateId)
                .GroupBy(m => m.IssueLineId)
                .Select(g => new { g.Key, Q = g.Sum(m => m.Quantity), C = g.Sum(m => m.TotalCost) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Q, x.C));
}
