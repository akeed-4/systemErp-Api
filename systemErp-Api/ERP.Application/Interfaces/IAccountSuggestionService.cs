using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public record AccountSuggestionDto(
    string AccountCode,
    string AccountNameAr,
    string Side, // Debit, Credit
    string Reason
);

public interface IAccountSuggestionService
{
    List<AccountSuggestionDto> GetInvoiceSuggestions(Invoice invoice);
    List<AccountSuggestionDto> GetVoucherSuggestions(Voucher voucher);
}
