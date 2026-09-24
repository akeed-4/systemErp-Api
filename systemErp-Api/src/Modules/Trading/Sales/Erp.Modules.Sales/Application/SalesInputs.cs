using Erp.Modules.Customers.Contracts;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel.Errors;

namespace Erp.Modules.Sales.Application;

internal sealed record PartySnapshot(Guid? CustomerId, string Name, string? VatNumber, string? CrNumber, string? Phone, string? Email, string? Address, Guid? AccountId, decimal CreditLimit);

/// <summary>Turns request lines and customer ids into priced line data and a buyer snapshot (shared by all sales documents).</summary>
internal sealed class SalesInputs(IProductCatalog products, ICustomerDirectory customers)
{
    public const decimal DefaultVatRate = 15m;

    public async Task<IReadOnlyList<LineData>> ResolveLinesAsync(IReadOnlyList<SalesLineInput> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
        {
            throw ErpException.Validation("At least one line is required.", "يجب إدخال سطر واحد على الأقل.");
        }

        var ids = lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var catalog = await products.FindManyAsync(ids, ct);

        return lines.Select((l, i) =>
        {
            if (l.ProductId is { } productId)
            {
                if (!catalog.TryGetValue(productId, out var product) || !product.IsActive)
                {
                    throw ErpException.Validation($"Line {i + 1}: unknown or inactive product.", $"السطر {i + 1}: الصنف غير موجود أو غير نشط.");
                }

                return new LineData(productId, string.IsNullOrWhiteSpace(l.Description) ? product.NameAr : l.Description.Trim(), l.Unit ?? product.UnitNameAr,
                    l.Quantity, l.UnitPrice, l.Discount, l.VatRate ?? product.VatRate, l.CostCenterId);
            }

            if (string.IsNullOrWhiteSpace(l.Description))
            {
                throw ErpException.Validation($"Line {i + 1}: a product or a description is required.", $"السطر {i + 1}: يجب اختيار صنف أو كتابة وصف.");
            }

            return new LineData(null, l.Description.Trim(), l.Unit ?? "خدمة", l.Quantity, l.UnitPrice, l.Discount, l.VatRate ?? DefaultVatRate, l.CostCenterId);
        }).ToList();
    }

    public async Task<PartySnapshot> ResolvePartyAsync(Guid? customerId, string? buyerName, CancellationToken ct)
    {
        if (customerId is not { } id)
        {
            return new PartySnapshot(null, string.IsNullOrWhiteSpace(buyerName) ? "عميل نقدي" : buyerName.Trim(), null, null, null, null, null, null, 0);
        }

        var customer = await customers.FindAsync(id, ct);
        if (customer is not { IsActive: true })
        {
            throw ErpException.Validation("Unknown or inactive customer.", "العميل غير موجود أو غير نشط.");
        }

        var address = string.Join(" - ", new[] { customer.City, customer.District, customer.Street, customer.BuildingNo, customer.PostalCode }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return new PartySnapshot(customer.Id, customer.NameAr, customer.VatNumber, customer.CrNumber, customer.Phone, customer.Email,
            address.Length == 0 ? null : address, customer.AccountId, customer.CreditLimit);
    }
}
