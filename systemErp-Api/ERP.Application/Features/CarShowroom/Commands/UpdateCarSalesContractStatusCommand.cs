using ERP.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Features.CarShowroom.Commands;

public record UpdateCarSalesContractStatusCommand(
    Guid Id,
    string Status,
    string? HandoverProtocolNumber,
    string? Notes
) : IRequest<bool>;

public class UpdateCarSalesContractStatusCommandHandler : IRequestHandler<UpdateCarSalesContractStatusCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICarShowroomService _carShowroomService;

    public UpdateCarSalesContractStatusCommandHandler(IApplicationDbContext context, ICarShowroomService carShowroomService)
    {
        _context = context;
        _carShowroomService = carShowroomService;
    }

    public async Task<bool> Handle(UpdateCarSalesContractStatusCommand request, CancellationToken cancellationToken)
    {
        var contract = await _context.CarSalesContracts.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (contract == null) return false;

        if (!string.IsNullOrEmpty(request.HandoverProtocolNumber))
        {
            contract.UpdateHandover(request.HandoverProtocolNumber, request.Notes);
        }

        await _carShowroomService.ProcessSalesContractStatusTransitionAsync(request.Id, request.Status, cancellationToken);
        return true;
    }
}
