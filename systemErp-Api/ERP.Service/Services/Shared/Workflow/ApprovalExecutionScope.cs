namespace ERP.Service.Services.Shared;

/// <summary>
/// تنفيذ عملية محجوزة بعد اعتمادها النهائي: يُنفّذها المعتمِد نيابةً عن مقدّم الطلب، وصلاحية مقدّم الطلب فُحصت عند طلبه (التجربة تمر
/// بالخدمة نفسها). فحوص الصلاحية داخل الخدمات تتخطى هذا التنفيذ، كما كان فحص المتحكم لا يمر به.
/// </summary>
public static class ApprovalExecutionScope
{
    private static readonly AsyncLocal<bool> Current = new();

    public static bool Active => Current.Value;

    public static IDisposable Enter()
    {
        var previous = Current.Value;
        Current.Value = true;
        return new Restore(previous);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
