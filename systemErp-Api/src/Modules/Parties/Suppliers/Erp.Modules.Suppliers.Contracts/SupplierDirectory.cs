namespace Erp.Modules.Suppliers.Contracts;

/// <summary>What purchasing modules need to validate a supplier and snapshot it on a document. Car agents are suppliers (§14 D6).</summary>
public sealed record SupplierSummary(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    string? VatNumber,
    string? CrNumber,
    string? Phone,
    string? Email,
    string? City,
    string? Address,
    int PaymentTermsDays,
    Guid? AccountId,
    bool IsActive);

public interface ISupplierDirectory
{
    Task<SupplierSummary?> FindAsync(Guid supplierId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, SupplierSummary>> FindManyAsync(IReadOnlyCollection<Guid> supplierIds, CancellationToken cancellationToken);
}
