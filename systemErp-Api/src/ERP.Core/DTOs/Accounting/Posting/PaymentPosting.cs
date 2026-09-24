using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

/// <summary>جزء من المبلغ سُوّي فوراً عبر حساب خزينة/بنك (نقد، بطاقة، تحويل...). الباقي يذهب إلى حساب الطرف (آجل).</summary>
public record PaymentPosting(string TreasuryAccountCode, decimal Amount);
