namespace ERP.Core.Models.Shared;

/// <summary>
/// مستند له طرف (عميل أو مورد). PartyId يبقى كما هو في الـ API، ويُوزَّع تلقائياً عند الحفظ على
/// CustomerId أو SupplierId المقيّدَين بمفتاح أجنبي، لأن قاعدة البيانات لا تقبل مفتاحاً أجنبياً واحداً يشير لجدولين.
/// </summary>
public interface IPartyDocument
{
    Guid? PartyId { get; }
    Guid? CustomerId { get; set; }
    Guid? SupplierId { get; set; }

    /// <summary>true إذا كان الطرف مورداً (مستند شراء أو PartyType = supplier).</summary>
    bool IsSupplierParty { get; }
}

public static class PartySide
{
    /// <summary>PartyType الصريح (customer/supplier) يُقدَّم، وإلا يُستنتج من جهة المستند.</summary>
    public static bool IsSupplier(string? partyType, bool purchaseSide) => partyType?.Trim().ToLowerInvariant() switch
    {
        "supplier" or "vendor" => true,
        "customer" or "client" => false,
        _ => purchaseSide,
    };

    public static void Sync(IPartyDocument d)
    {
        d.CustomerId = d.PartyId.HasValue && !d.IsSupplierParty ? d.PartyId : null;
        d.SupplierId = d.PartyId.HasValue && d.IsSupplierParty ? d.PartyId : null;
    }
}
