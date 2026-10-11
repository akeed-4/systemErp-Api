namespace ERP.Api.Infrastructure;

/// <summary>
/// الـ controller (أو الإجراء) يخص وحدة مرخّصة: يكفي أن يشمل اشتراك المنشأة واحدة من الوحدات المذكورة.
/// ما لا يحمل هذه السمة مشترك بين كل الوحدات (المحاسبة، البيانات الأساسية، الفواتير، المخزون، الإعدادات).
/// يطبّقها <see cref="SubscriptionGateFilter"/>؛ وسمة الإجراء تتقدّم على سمة الـ controller.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireModuleAttribute : Attribute
{
    public string[] AnyOf { get; }
    public RequireModuleAttribute(params string[] anyOf) => AnyOf = anyOf;
}
