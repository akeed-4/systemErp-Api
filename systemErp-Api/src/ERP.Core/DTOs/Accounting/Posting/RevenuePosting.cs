namespace ERP.Core.DTOs.Accounting;

/// <summary>جزء من إيراد المستند على حساب ومركز تكلفة بعينهما (حساب فارغ = حساب إيراد المستند ثم الافتراضي).</summary>
public record RevenuePosting(string? AccountCode, Guid? CostCenterId, decimal Amount);
