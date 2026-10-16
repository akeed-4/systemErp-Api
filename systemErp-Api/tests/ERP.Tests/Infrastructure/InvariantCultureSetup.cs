using System.Globalization;
using System.Runtime.CompilerServices;

namespace ERP.Tests.Infrastructure;

/// <summary>
/// الاختبارات تكوّن التواريخ نصاً (فترات الرواتب، نطاقات التقارير): على جهاز بإعداد منطقة عربي (ar-SA) يصير التقويم الافتراضي
/// هجرياً فتخرج «1448-04» بدل «2026-09». ثقافة ثابتة لكل خيوط الاختبار تجعل النتائج مستقلة عن إعدادات الجهاز.
/// </summary>
internal static class InvariantCultureSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
