namespace ERP.Core.Models.Shared;

/// <summary>التصنيف الضريبي لسطر الفاتورة كما يظهر في الإقرار الضريبي (يطابق فئات ZATCA: S / Z / E / O).</summary>
public enum VatCategory
{
    /// <summary>خاضع للنسبة الأساسية.</summary>
    Standard = 1,
    /// <summary>خاضع لنسبة الصفر (صادرات وما في حكمها).</summary>
    ZeroRated = 2,
    /// <summary>معفى.</summary>
    Exempt = 3,
    /// <summary>خارج نطاق الضريبة.</summary>
    OutOfScope = 4
}
