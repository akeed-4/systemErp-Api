namespace Erp.SharedKernel.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Role { get; }

    string? Email { get; }

    string? Name { get; }
}

public enum ScreenAction
{
    View,
    Create,
    Edit,
    Delete,
    Approve,
}

/// <summary>Evaluates the frontend's ScreenPermission model (screenId × action) for the current user.</summary>
public interface IScreenPermissionChecker
{
    Task<bool> IsAllowedAsync(string screenId, ScreenAction action, CancellationToken cancellationToken);
}

/// <summary>The frontend's SYSTEM_SCREENS ids (permission.service.ts). Master rows live in catalog.Screens.</summary>
public static class ScreenIds
{
    public const string Dashboard = "dashboard";
    public const string MasterData = "master-data";
    public const string Sales = "sales";
    public const string SalesReturns = "sales-returns";
    public const string Purchases = "purchases";
    public const string Vouchers = "vouchers";
    public const string Accounts = "accounts";
    public const string Zatca = "zatca";
    public const string CarShowroom = "car-showroom";
    public const string Reports = "reports";
    public const string ApprovalPolicies = "approval-policies";
    public const string UserPermissions = "user-permissions";
}

public static class SystemRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string GeneralManager = "general_manager";
    public const string ChiefAccountant = "chief_accountant";
    public const string SalesRep = "sales_rep";
}
