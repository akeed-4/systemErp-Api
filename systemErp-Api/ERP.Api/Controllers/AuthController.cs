using Microsoft.AspNetCore.Mvc;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;

namespace ERP.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AuthController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // التحقق من صحة بيانات الدخول والشركة
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { Message = "البريد الإلكتروني وكلمة المرور مطلوبان." });
        }

        // إرجاع رمز الوصول وتفاصيل المستخدم والمستأجر
        return Ok(new
        {
            Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.dummy_token_ksa_erp_2026",
            ExpiresIn = 86400,
            User = new
            {
                Email = request.Email,
                FullName = "عبدالله السبيعي",
                Role = "Owner",
                TenantId = request.TenantId ?? "tenant-1"
            },
            Message = "تم تسجيل الدخول بنجاح"
        });
    }

    [HttpPost("register-company")]
    public async Task<IActionResult> RegisterCompany([FromBody] RegisterCompanyRequest request)
    {
        // 1. التحقق من الرقم الضريبي (15 رقم يبدأ وينتهي بـ 3)
        if (string.IsNullOrWhiteSpace(request.VatNumber) || request.VatNumber.Length != 15 || 
            !request.VatNumber.StartsWith("3") || !request.VatNumber.EndsWith("3"))
        {
            return BadRequest(new { Message = "الرقم الضريبي السعودي يجب أن يتكون من 15 خانة ويبدأ وينتهي بالرقم 3 (ZATCA Compliant)." });
        }

        // 2. إنشاء المستأجر الجديد
        var newTenant = new Tenant
        {
            NameAr = request.CompanyNameAr,
            NameEn = request.CompanyNameEn ?? request.CompanyNameAr,
            VatNumber = request.VatNumber,
            CrNumber = request.CrNumber,
            Address = request.Address,
            City = request.City,
            IsActive = true
        };
        _context.Tenants.Add(newTenant);

        // 3. ربط باقة الاشتراك وتوليد السجل
        var subscription = new Subscription
        {
            TenantId = newTenant.Id,
            PlanType = request.PlanType,
            PlanNameAr = request.PlanType == SubscriptionPlanId.Starter ? "باقة البداية" :
                         request.PlanType == SubscriptionPlanId.Professional ? "باقة الشركات المتقدمة" : "باقة المؤسسات",
            BillingCycle = request.BillingCycle,
            StartDate = DateTime.UtcNow,
            ExpiryDate = request.BillingCycle == SubscriptionBillingCycle.Yearly ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1),
            Status = SubscriptionStatus.Active,
            PaymentMethod = request.PaymentMethod,
            TransactionReference = $"TXN-{Guid.NewGuid().ToString()[..8].ToUpper()}"
        };
        _context.Subscriptions.Add(subscription);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            Success = true,
            TenantId = newTenant.Id,
            Message = $"تم تسجيل منشأة '{newTenant.NameAr}' وتفعيل الاشتراك بنجاح!",
            Subscription = subscription
        });
    }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? TenantId { get; set; }
}

public class RegisterCompanyRequest
{
    public string CompanyNameAr { get; set; } = string.Empty;
    public string? CompanyNameEn { get; set; }
    public string VatNumber { get; set; } = string.Empty;
    public string CrNumber { get; set; } = string.Empty;
    public string City { get; set; } = "الرياض";
    public string Address { get; set; } = string.Empty;
    public string AdminName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPhone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public SubscriptionPlanId PlanType { get; set; } = SubscriptionPlanId.Professional;
    public SubscriptionBillingCycle BillingCycle { get; set; } = SubscriptionBillingCycle.Yearly;
    public string PaymentMethod { get; set; } = "Mada";
}
