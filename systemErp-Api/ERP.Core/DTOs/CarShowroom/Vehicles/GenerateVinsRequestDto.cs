namespace ERP.Core.DTOs.CarShowroom;

/** طلب توليد أرقام شاسيه مؤقتة (TMP + عدّاد) لسيارات فاتورة الشراء قبل معرفة أرقامها الحقيقية. */
public class GenerateVinsRequestDto
{
    public int Count { get; set; }
}
