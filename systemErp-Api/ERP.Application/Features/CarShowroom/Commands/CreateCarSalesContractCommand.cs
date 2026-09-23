using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using MediatR;

namespace ERP.Application.Features.CarShowroom.Commands;

public record CreateCarSalesContractCommand(
    string CycleType, // individual | corporate | bank_lease | installment
    string BuyerType, // individual | corporate | government
    string BuyerName,
    string BuyerNationalIdOrCr,
    string BuyerPhone,
    string? BuyerEmail,
    string? BuyerAddress,
    string? FinancingBankName,
    decimal? DownPaymentAmount,
    decimal? FinancedAmount,
    Guid VehicleId,
    string Vin,
    string VehicleDescription,
    decimal CostPrice,
    decimal SellingPrice,
    decimal VatAmount,
    decimal TotalWithVat,
    string PaymentMethod,
    string Status, // draft | approved | allocated | delivered | invoiced | cancelled
    string? Notes,
    string? HandoverProtocolNumber,
    string? SalespersonName
) : IRequest<Guid>;

public class CreateCarSalesContractCommandHandler : IRequestHandler<CreateCarSalesContractCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICarShowroomService _carShowroomService;

    public CreateCarSalesContractCommandHandler(IApplicationDbContext context, ICarShowroomService carShowroomService)
    {
        _context = context;
        _carShowroomService = carShowroomService;
    }

    public async Task<Guid> Handle(CreateCarSalesContractCommand request, CancellationToken cancellationToken)
    {
        var cycleType = Enum.TryParse<CarSalesCycleType>(request.CycleType, true, out var cycle) ? cycle : CarSalesCycleType.Individual;
        var buyerType = Enum.TryParse<BuyerType>(request.BuyerType, true, out var buyer) ? buyer : BuyerType.Individual;
        var status = Enum.TryParse<SalesContractStatus>(request.Status, true, out var st) ? st : SalesContractStatus.Draft;

        var contract = new CarSalesContract
        {
            ContractNumber = $"CTR-{DateTime.UtcNow:yyyyMMddHHmmss}",
            CycleType = cycleType,
            Date = DateTime.UtcNow,
            BuyerType = buyerType,
            BuyerName = request.BuyerName,
            BuyerNationalIdOrCr = request.BuyerNationalIdOrCr,
            BuyerPhone = request.BuyerPhone,
            BuyerEmail = request.BuyerEmail,
            BuyerAddress = request.BuyerAddress,
            FinancingBankName = request.FinancingBankName,
            DownPaymentAmount = request.DownPaymentAmount,
            FinancedAmount = request.FinancedAmount,
            VehicleId = request.VehicleId,
            Vin = request.Vin,
            VehicleDescription = request.VehicleDescription,
            CostPrice = request.CostPrice,
            SellingPrice = request.SellingPrice,
            VatAmount = request.VatAmount,
            TotalWithVat = request.TotalWithVat,
            PaymentMethod = request.PaymentMethod,
            Status = status,
            Notes = request.Notes,
            HandoverProtocolNumber = request.HandoverProtocolNumber,
            SalespersonName = request.SalespersonName,
        };

        _context.CarSalesContracts.Add(contract);
        await _context.SaveChangesAsync(cancellationToken);

        if (status != SalesContractStatus.Draft)
        {
            await _carShowroomService.ProcessSalesContractStatusTransitionAsync(contract.Id, request.Status, cancellationToken);
        }

        return contract.Id;
    }
}
