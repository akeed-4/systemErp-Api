using System.Text;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>ترميز TLV للمرحلة الأولى من ZATCA (الوسوم 1-5) بصيغة Base64 - يطابق zatca-tlv.util.ts في الواجهة.</summary>
public static class ZatcaTlv
{
    public static string Encode(string sellerName, string vatNumber, DateTime timestamp, decimal total, decimal vat)
    {
        var bytes = new List<byte>();
        void Add(int tag, string value)
        {
            var data = Encoding.UTF8.GetBytes(value);
            if (data.Length > 255) data = data[..255];
            bytes.Add((byte)tag);
            bytes.Add((byte)data.Length);
            bytes.AddRange(data);
        }
        Add(1, sellerName);
        Add(2, vatNumber);
        Add(3, timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        Add(4, total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        Add(5, vat.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToBase64String(bytes.ToArray());
    }
}
