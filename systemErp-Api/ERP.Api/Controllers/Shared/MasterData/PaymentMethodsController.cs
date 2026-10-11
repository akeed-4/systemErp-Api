using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/paymentmethods"), RequireScreen("master-data")]
public class PaymentMethodsController : CrudController<PaymentMethodItemDto, CreatePaymentMethodItemDto, UpdatePaymentMethodItemDto>
{
    public PaymentMethodsController(IPaymentMethodService s) : base(s) { }
}
