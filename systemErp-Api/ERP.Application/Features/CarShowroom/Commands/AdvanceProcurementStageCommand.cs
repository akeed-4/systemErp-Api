using ERP.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Features.CarShowroom.Commands;

public record AdvanceProcurementStageCommand(
    Guid Id,
    string Stage // next stage
) : IRequest<bool>;

public class AdvanceProcurementStageCommandHandler : IRequestHandler<AdvanceProcurementStageCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICarShowroomService _carShowroomService;

    public AdvanceProcurementStageCommandHandler(IApplicationDbContext context, ICarShowroomService carShowroomService)
    {
        _context = context;
        _carShowroomService = carShowroomService;
    }

    public async Task<bool> Handle(AdvanceProcurementStageCommand request, CancellationToken cancellationToken)
    {
        var exists = await _context.CarProcurementOrders.AnyAsync(o => o.Id == request.Id, cancellationToken);
        if (!exists) return false;

        await _carShowroomService.ProcessProcurementStageTransitionAsync(request.Id, request.Stage, cancellationToken);
        return true;
    }
}
