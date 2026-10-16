using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>طرق الدفع المؤسَّسة لقوائم المستندات: لأي مستخدم مسجّل (الإدارة الكاملة في /paymentmethods تتطلب البيانات الأساسية).</summary>
[Route("api/v1/lookups/payment-methods")]
public class PaymentMethodOptionsController : ErpControllerBase
{
    private readonly IPaymentMethodService _methods;
    public PaymentMethodOptionsController(IPaymentMethodService methods) => _methods = methods;

    [HttpGet]
    public async Task<IActionResult> Active(CancellationToken ct) => Success(await _methods.ListActiveAsync(ct));
}
