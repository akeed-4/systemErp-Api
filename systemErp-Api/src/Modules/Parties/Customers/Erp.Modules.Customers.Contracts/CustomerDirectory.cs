namespace Erp.Modules.Customers.Contracts;

public enum CustomerType
{
    Individual,
    Corporate,
    Government,
}

/// <summary>What selling modules need to validate a customer and snapshot it on a document.</summary>
public sealed record CustomerSummary(
    Guid Id,
    string Code,
    CustomerType Type,
    string NameAr,
    string NameEn,
    string? VatNumber,
    string? CrNumber,
    string? NationalId,
    string? Phone,
    string? Email,
    string? City,
    string? District,
    string? Street,
    string? BuildingNo,
    string? PostalCode,
    decimal CreditLimit,
    int CreditPeriodDays,
    Guid? AccountId,
    bool IsActive);

public interface ICustomerDirectory
{
    Task<CustomerSummary?> FindAsync(Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, CustomerSummary>> FindManyAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken);
}
