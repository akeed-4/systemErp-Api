using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

/// <summary>قواعد مشتركة لتصحيح مستندات الورديات: من يحق له، وإعادة حساب فرق الصندوق.</summary>
internal static class PosShiftRules
{
    public static readonly HashSet<string> PrivilegedRoles = new() { "owner", "admin", "general_manager", "chief_accountant" };

    public static bool IsPrivileged(ICurrentUser user) => PrivilegedRoles.Contains(user.RoleId ?? string.Empty);

    /// <summary>الوردية المفتوحة يصحّحها كاشيرها؛ المغلقة تتطلب دوراً إدارياً.</summary>
    public static void EnsureCanCorrect(PosShift shift, ICurrentUser user)
    {
        if (IsPrivileged(user)) return;
        if (shift.Status != PosShiftStatus.Open || shift.CashierId != user.UserId)
            throw new ForbiddenException("تصحيح مستندات وردية مغلقة أو لكاشير آخر للأدوار الإدارية فقط.");
    }

    public static void RecomputeVariance(PosShift shift)
    {
        if (shift.Status == PosShiftStatus.Closed && shift.ClosingCashActual.HasValue)
            shift.CashVariance = shift.ClosingCashActual.Value - (shift.OpeningCash + shift.TotalCashSales - shift.TotalCashRefunds);
    }
}
