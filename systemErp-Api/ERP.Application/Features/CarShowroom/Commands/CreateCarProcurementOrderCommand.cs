using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using MediatR;

namespace ERP.Application.Features.CarShowroom.Commands;

public record ProcurementItemDto(
    string BrandName,
    string ModelName,
    string? TrimName,
    int Year,
    int Quantity,
    decimal UnitPrice,
    string? AssignedVins
);

public record CreateCarProcurementOrderCommand(
    string Stage, // requisition | requisition_approved | rfq | rfq_approved | purchase_order | vin_received | invoiced
    Guid SupplierId,
    string SupplierName,
    string PaymentType,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    string Status, // draft | approved | in_progress | received | invoiced | closed | rejected
    string? Notes,
    List<ProcurementItemDto> Items
) : IRequest<Guid>;

public class CreateCarProcurementOrderCommandHandler : IRequestHandler<CreateCarProcurementOrderCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICarShowroomService _carShowroomService;

    public CreateCarProcurementOrderCommandHandler(IApplicationDbContext context, ICarShowroomService carShowroomService)
    {
        _context = context;
        _carShowroomService = carShowroomService;
    }

    public async Task<Guid> Handle(CreateCarProcurementOrderCommand request, CancellationToken cancellationToken)
    {
        var stage = Enum.TryParse<ProcurementStage>(request.Stage, true, out var stg) ? stg : ProcurementStage.Requisition;
        var status = Enum.TryParse<ProcurementOrderStatus>(request.Status, true, out var stat) ? stat : ProcurementOrderStatus.Draft;

        var order = new CarProcurementOrder
        {
            OrderNumber = $"REQ-{DateTime.UtcNow:yyyyMMddHHmmss}",
            Stage = stage,
            Date = DateTime.UtcNow,
            SupplierId = request.SupplierId,
            SupplierName = request.SupplierName,
            PaymentType = request.PaymentType,
            Subtotal = request.Subtotal,
            VatTotal = request.VatTotal,
            GrandTotal = request.GrandTotal,
            Status = status,
            Notes = request.Notes,
        };

        foreach (var item in request.Items)
        {
            order.Items.Add(new CarProcurementOrderItem
            {
                BrandName = item.BrandName,
                ModelName = item.ModelName,
                TrimName = item.TrimName,
                Year = item.Year,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                AssignedVins = item.AssignedVins,
            });
        }

        _context.CarProcurementOrders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        if (stage != ProcurementStage.Requisition)
        {
            await _carShowroomService.ProcessProcurementStageTransitionAsync(order.Id, request.Stage, cancellationToken);
        }

        return order.Id;
    }
}
