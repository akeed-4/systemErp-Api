namespace ERP.Api.Infrastructure;

/// <summary>
/// الـ controller (أو الإجراء) ميزة متقدمة في الباقة (<see cref="PlanFeatures"/>): يتطلب أن يشملها اشتراك المنشأة.
/// تكمّل <see cref="RequireModuleAttribute"/> ولا تغني عنها: الوحدة أولاً ثم الميزة داخلها.
/// يطبّقها <see cref="SubscriptionGateFilter"/>؛ وسمة الإجراء تتقدّم على سمة الـ controller.
/// <see cref="WritesOnly"/>: القراءة متاحة لكل الباقات (قوائم اختيار تقرؤها شاشات أساسية) والإضافة والتعديل والحذف للميزة.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireFeatureAttribute : Attribute
{
    public string Feature { get; }
    public bool WritesOnly { get; init; }
    public RequireFeatureAttribute(string feature) => Feature = feature;
}
