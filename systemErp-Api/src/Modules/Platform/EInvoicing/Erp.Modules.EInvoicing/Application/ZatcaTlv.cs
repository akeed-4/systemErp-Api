using System.Globalization;
using System.Text;

namespace Erp.Modules.EInvoicing.Application;

/// <summary>
/// ZATCA QR payload: TLV (1-byte tag, 1-byte length, UTF-8 value) then Base64 — byte-for-byte the frontend's zatca-tlv.util.ts.
/// Phase 1 uses tags 1–5 (seller, VAT number, timestamp, total with VAT, VAT); Phase 2 adds 6–9 (hash, signature, key, stamp).
/// </summary>
internal static class ZatcaTlv
{
    public static string Encode(string sellerName, string vatNumber, DateTimeOffset timestamp, decimal totalWithVat, decimal vatTotal, string? invoiceHash = null)
    {
        var tags = new List<(byte Tag, string Value)>
        {
            (1, string.IsNullOrWhiteSpace(sellerName) ? "مؤسسة تجارية" : sellerName),
            (2, vatNumber),
            (3, timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            (4, totalWithVat.ToString("0.00", CultureInfo.InvariantCulture)),
            (5, vatTotal.ToString("0.00", CultureInfo.InvariantCulture)),
        };
        if (invoiceHash is not null)
        {
            tags.Add((6, invoiceHash));
        }

        using var buffer = new MemoryStream();
        foreach (var (tag, value) in tags)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > byte.MaxValue)
            {
                throw new ArgumentException($"TLV value of tag {tag} is longer than 255 bytes.", nameof(sellerName));
            }

            buffer.WriteByte(tag);
            buffer.WriteByte((byte)bytes.Length);
            buffer.Write(bytes);
        }

        return Convert.ToBase64String(buffer.ToArray());
    }

    public static IReadOnlyDictionary<int, string> Decode(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var result = new Dictionary<int, string>();
        for (var i = 0; i + 1 < bytes.Length;)
        {
            int tag = bytes[i];
            int length = bytes[i + 1];
            result[tag] = Encoding.UTF8.GetString(bytes, i + 2, length);
            i += 2 + length;
        }

        return result;
    }
}
