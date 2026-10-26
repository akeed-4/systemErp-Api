namespace ERP.Core.Models.Shared;

/// <summary>نطاق السجلات التي يصل إليها الدور/المستخدم في المستندات ذات النطاق (<see cref="IScopedRecord"/>).</summary>
public enum DataScope
{
    /// <summary>جميع الفروع.</summary>
    All = 0,
    /// <summary>فروع (مستودعات) محددة.</summary>
    Branches = 1,
    /// <summary>سجلات أنشأها مستخدمو قسم المستخدم.</summary>
    Department = 2,
    /// <summary>السجلات التي أنشأها المستخدم.</summary>
    Own = 3,
    /// <summary>السجلات المسندة إليه.</summary>
    Assigned = 4,
}
