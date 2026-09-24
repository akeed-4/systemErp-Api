namespace ERP.Core.Contracts.Shared;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Name { get; }
    /// <summary>owner | admin | general_manager | chief_accountant | sales_rep</summary>
    string? RoleId { get; }
    bool IsAuthenticated { get; }
}
