using ERP.Domain.Enums;

namespace ERP.Application.Invoices;

public record InvoiceResponseDto(
    string Id,
    string InvoiceNumber,
    string TenantId,
    InvoiceType Type,
    DateTime IssueDate,
    string PartyName,
    string? PartyVatNumber,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    decimal GrossProfit,
    ZatcaSubmissionStatus ZatcaStatus,
    string? QrCodeTlvBase64
);

public record CreateInvoiceRequestDto(
    InvoiceType Type,
    string PartyName,
    string? PartyVatNumber,
    string? PartyAddress,
    List<CreateInvoiceItemRequestDto> Items
);

public record CreateInvoiceItemRequestDto(
    string? ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount = 0m,
    decimal VatRatePercent = 15m
);
