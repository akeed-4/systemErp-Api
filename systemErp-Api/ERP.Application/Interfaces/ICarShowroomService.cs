using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public interface ICarShowroomService
{
    // Procurement Cycle
    Task ProcessProcurementStageTransitionAsync(Guid orderId, string nextStage, CancellationToken ct = default);
    Task SyncVehiclesFromProcurementOrderAsync(Guid orderId, CancellationToken ct = default);
    
    // Sales Cycle
    Task ProcessSalesContractStatusTransitionAsync(Guid contractId, string nextStatus, CancellationToken ct = default);
    Task GenerateInvoiceFromContractAsync(Guid contractId, CancellationToken ct = default);
}
