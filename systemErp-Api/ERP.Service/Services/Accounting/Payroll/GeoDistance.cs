namespace ERP.Service.Services.Accounting;

/// <summary>
/// المسافة على سطح الأرض بين نقطتين بمعادلة haversine (نصف القطر الوسطي 6,371,008.8 م). خطؤها أقل من 0.5% مقارنة
/// بالمجسم الإهليلجي، وهو دون دقة GPS للهاتف بكثير في نطاقات الحضور (عشرات إلى آلاف الأمتار).
/// </summary>
public static class GeoDistance
{
    private const double EarthRadiusMeters = 6_371_008.8;

    public static bool IsValidCoordinate(decimal latitude, decimal longitude)
        => latitude is >= -90m and <= 90m && longitude is >= -180m and <= 180m;

    public static double Meters(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        static double Rad(decimal degrees) => (double)degrees * Math.PI / 180d;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }
}
