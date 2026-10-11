using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public record PostingLine(string AccountCode, decimal Debit, decimal Credit, string? Notes = null, Guid? CostCenterId = null);
