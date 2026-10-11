namespace ERP.Core.Contracts.Shared;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Name { get; }
    string? Email { get; }
    /// <summary>owner | admin | general_manager | chief_accountant | sales_rep | employee</summary>
    string? RoleId { get; }
    bool IsAuthenticated { get; }
}
