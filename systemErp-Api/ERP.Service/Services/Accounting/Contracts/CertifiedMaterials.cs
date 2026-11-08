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

    /// <summary>المختار من كل سطر إذن (كمية وتكلفة) في المستخلصات غير الملغاة، عدا مستخلص بعينه عند تعديله.</summary>
    public static async Task<Dictionary<Guid, (decimal Quantity, decimal Cost)>> ByIssueLineAsync(ErpDbContext db, Guid contractId,
        Guid? exceptCertificateId, CancellationToken ct)
        => (await Active(db, contractId, exceptCertificateId)
                .GroupBy(m => m.IssueLineId)
                .Select(g => new { g.Key, Q = g.Sum(m => m.Quantity), C = g.Sum(m => m.TotalCost) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Q, x.C));
}
