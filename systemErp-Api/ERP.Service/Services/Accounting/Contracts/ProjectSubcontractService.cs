using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using CertSt = ERP.Core.Models.Accounting.ContractCertificateStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مقاولو الباطن لمشروع (عقد عميل بجدول كميات): عقود الموردين التي تشير إليه (<see cref="CommercialContract.ParentContractId"/>)
/// غير الملغاة، بموقف مستخلصاتها (المعتمدة والمفوترة = تكلفة الباطن المستحقة)، وتغطية بنود المشروع بأسطر الباطن المرتبطة بها
/// (<see cref="CommercialContractItem.MainContractItemId"/>) بالكمية المعدلة. قراءة فقط من البيانات المحفوظة.
/// </summary>
public class ProjectSubcontractService : IProjectSubcontractService
{
    private readonly ErpDbContext _db;

    public ProjectSubcontractService(ErpDbContext db) => _db = db;

    public async Task<ProjectSubcontractsDto> GetAsync(Guid contractId, CancellationToken ct = default)
    {
        var project = await _db.Set<CommercialContract>().AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);

        var subs = await _db.Set<CommercialContract>().AsNoTracking().Include(c => c.Items)
            .Where(c => c.ParentContractId == contractId && c.Stage != ContractStageDefinition.Cancelled)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var subIds = subs.Select(s => s.Id).ToList();

        var certs = await _db.Set<ContractCertificate>().AsNoTracking()
            .Where(c => subIds.Contains(c.ContractId) && c.Status != CertSt.Cancelled)
            .Select(c => new { c.Id, c.ContractId, c.Status, c.CurrentWorkValue, c.TotalWithVat, c.RetentionAmount }).ToListAsync(ct);
        var counted = certs.Where(c => c.Status is CertSt.Approved or CertSt.Invoiced).ToList();
        var countedIds = counted.Select(c => c.Id).ToList();
        var subCertified = await _db.Set<ContractCertificateLine>().AsNoTracking()
            .Where(l => countedIds.Contains(l.CertificateId))
            .GroupBy(l => l.ContractItemId).Select(g => new { g.Key, Qty = g.Sum(l => l.CurrentQuantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var mainCertified = await _db.Set<ContractCertificateLine>().AsNoTracking()
            .Where(l => l.Certificate.ContractId == contractId && (l.Certificate.Status == CertSt.Approved || l.Certificate.Status == CertSt.Invoiced))
            .GroupBy(l => l.ContractItemId).Select(g => new { g.Key, Qty = g.Sum(l => l.CurrentQuantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        var rows = subs.Select(s =>
        {
            var mine = counted.Where(c => c.ContractId == s.Id).ToList();
            var certified = mine.Sum(c => c.CurrentWorkValue);
            return new ProjectSubcontractRowDto
            {
                Id = s.Id, ContractNumber = s.ContractNumber, Title = s.Title, PartyName = s.PartyName, Stage = s.Stage,
                StartDate = s.StartDate, EndDate = s.EndDate, ContractValue = s.ContractValue, CertifiedValue = certified,
                CompletionPercent = s.ContractValue > 0 ? Math.Round(certified / s.ContractValue * 100m, 2) : 0,
                InvoicedTotal = mine.Where(c => c.Status == CertSt.Invoiced).Sum(c => c.TotalWithVat),
                RetentionHeld = mine.Sum(c => c.RetentionAmount),
                OpenCertificates = certs.Count(c => c.ContractId == s.Id && CertSt.IsOpen(c.Status)),
                UnlinkedLines = s.Items.Count(i => !i.IsGroup && i.MainContractItemId == null),
            };
        }).ToList();

        var linked = subs.SelectMany(s => s.Items.Where(i => !i.IsGroup && i.MainContractItemId != null).Select(i => (Sub: s, Line: i)))
            .GroupBy(x => x.Line.MainContractItemId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var items = project.Items.Where(i => !i.IsGroup).OrderBy(i => i.SortOrder).Select(i =>
        {
            var lines = linked.GetValueOrDefault(i.Id) ?? new();
            var qty = lines.Sum(x => x.Line.RevisedQuantity);
            var value = lines.Sum(x => DocumentPricing.Round(x.Line.RevisedQuantity * x.Line.UnitPrice));
            var subPrice = qty > 0 ? Math.Round(value / qty, 4) : 0;
            return new SubcontractItemCoverageDto
            {
                MainItemId = i.Id, LineCode = i.LineCode, Description = i.Description, Unit = i.UnitDescription ?? i.Unit,
                MainQuantity = i.RevisedQuantity, MainUnitPrice = i.UnitPrice, MainCertifiedQuantity = mainCertified.GetValueOrDefault(i.Id),
                SubcontractedQuantity = qty, SubcontractValue = value, SubcontractUnitPrice = subPrice,
                SubcontractCertifiedQuantity = lines.Sum(x => subCertified.GetValueOrDefault(x.Line.Id)),
                SubcontractUnits = string.Join("، ", lines.Select(x => x.Line.UnitDescription ?? x.Line.Unit).Distinct()),
                UnitMargin = qty > 0 ? i.UnitPrice - subPrice : 0,
                OverSubcontracted = qty > i.RevisedQuantity + 0.00005m,
                SubcontractNumbers = lines.Select(x => x.Sub.ContractNumber).Distinct().ToList(),
            };
        }).Where(x => x.SubcontractedQuantity > 0).ToList();

        return new ProjectSubcontractsDto
        {
            ProjectValue = project.ContractValue,
            SubcontractValue = rows.Sum(r => r.ContractValue),
            CertifiedValue = rows.Sum(r => r.CertifiedValue),
            InvoicedTotal = rows.Sum(r => r.InvoicedTotal),
            RetentionHeld = rows.Sum(r => r.RetentionHeld),
            Subcontracts = rows,
            Items = items,
        };
    }
}
