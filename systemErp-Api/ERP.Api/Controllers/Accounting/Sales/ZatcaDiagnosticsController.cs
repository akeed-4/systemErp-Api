using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// تشخيص ZATCA المحلي المؤقت (<see cref="IZatcaLocalSelfTest"/>): لا يتصل بالهيئة ولا يكتب في القاعدة ولا يمس جلسات
/// الربط. ثلاث طبقات حماية: معطّل افتراضياً (404 ما لم يُفعَّل <see cref="EnabledSetting"/>)، صلاحية مستقلة حسّاسة
/// (<c>zatca-diagnostics</c>)، والمالك أو مدير النظام فقط.
/// </summary>
[Route("api/v1/diagnostics/zatca")]
[ZatcaSelfTestEnabled]
public class ZatcaDiagnosticsController : ErpControllerBase
{
    public const string EnabledSetting = "Diagnostics:ZatcaSelfTest:Enabled";
    public const string ScreenId = "zatca-diagnostics";

    private readonly IZatcaLocalSelfTest _selfTest;
    private readonly ICurrentUser _user;

    public ZatcaDiagnosticsController(IZatcaLocalSelfTest selfTest, ICurrentUser user)
    {
        _selfTest = selfTest; _user = user;
    }

    /// <summary>يشغّل المراحل الثماني ويعيد نتيجة كل مرحلة بلا Stack Trace (التفاصيل الكاملة في سجل الخادم).</summary>
    [HttpPost("self-test"), RequireScreen(ScreenId, ScreenAction.Edit)]
    public async Task<IActionResult> SelfTest(CancellationToken ct)
    {
        // الصلاحية الحسّاسة تُمنح افتراضياً لأدوار إدارية أوسع (المحاسب الرئيسي، المدير العام): التشخيص للمالك والمدير فقط
        if (_user.RoleId is not ("owner" or "admin")) throw new ForbiddenException();
        return Success(await _selfTest.RunAsync(ct));
    }
}

/// <summary>
/// يُخفي نقطة التشخيص كلياً (404) ما لم يُفعَّل الإعداد صراحةً، قبل أي فحص صلاحية، فلا يكشف وجودها وهي معطّلة.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ZatcaSelfTestEnabledAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue<bool>(ZatcaDiagnosticsController.EnabledSetting)) context.Result = new NotFoundResult();
    }
}
