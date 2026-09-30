using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

internal static class PartyValidation
{
    public static void Validate(string nameAr, string? vat, string? email, decimal opening, List<string> extra)
    {
        var errors = new List<string>(extra);
        if (string.IsNullOrWhiteSpace(nameAr)) errors.Add(Messages.ArabicNameRequired);
        if (!string.IsNullOrWhiteSpace(vat) && !SaudiVat.IsValid(vat))
            errors.Add(Messages.VatNumberFormat);
        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@')) errors.Add(Messages.EmailInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }
}
