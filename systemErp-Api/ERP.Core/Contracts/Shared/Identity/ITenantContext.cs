namespace ERP.Core.Contracts.Shared;

/// <summary>المنشأة (Tenant) الحالية للطلب. يحدّدها الـ JWT للمستخدم المصادَق عليه.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    void SetTenant(Guid tenantId);
}
