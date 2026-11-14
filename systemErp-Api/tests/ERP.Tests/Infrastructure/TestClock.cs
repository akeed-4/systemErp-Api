namespace ERP.Tests.Infrastructure;

/// <summary>
/// ساعة الخادم في الاختبارات: الوقت الحقيقي ما لم يُثبَّت. يثبّتها الاختبار بـ <see cref="Freeze"/> ضمن using فتعود عند الخروج.
/// يستعملها ما يحقن <see cref="TimeProvider"/> وحده (الحضور الميداني).
/// </summary>
public sealed class TestClock : TimeProvider
{
    public static readonly TestClock Instance = new();
    private DateTimeOffset? _frozen;

    private TestClock() { }

    public override DateTimeOffset GetUtcNow() => _frozen ?? base.GetUtcNow();

    public IDisposable Freeze(DateTime utc)
    {
        _frozen = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return new Release(this);
    }

    public void Set(DateTime utc) => _frozen = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private sealed class Release(TestClock clock) : IDisposable
    {
        public void Dispose() => clock._frozen = null;
    }
}
