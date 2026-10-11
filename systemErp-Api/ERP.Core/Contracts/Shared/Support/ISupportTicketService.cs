using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ISupportTicketService : ICrudService<SupportTicketDto, CreateSupportTicketDto, UpdateSupportTicketDto>
{
    Task<SupportTicketDto> AddReplyAsync(Guid id, AddSupportTicketReplyDto request, CancellationToken ct = default);
    Task<SupportTicketDto> ChangeStatusAsync(Guid id, ChangeSupportTicketStatusDto request, CancellationToken ct = default);
}
