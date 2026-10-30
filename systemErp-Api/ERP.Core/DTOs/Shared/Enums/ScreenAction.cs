namespace ERP.Core.DTOs.Shared;

/// <summary>
/// إجراءات الصلاحية. الخمسة الأولى هي الأصلية (أعمدة CanView..CanApprove)؛ وما بعدها إجراءات مستقلة عن CRUD
/// تُحفظ في <c>ScreenPermissionItem.Actions</c>. تُضاف القيم الجديدة في النهاية فقط حفاظاً على القيم الرقمية.
/// </summary>
public enum ScreenAction { View, Create, Edit, Delete, Approve, Review, Reject, Cancel, Sign, Close, Reopen, Post, Import, Print, Export, Pdf, Discount, Submit, LegalReview, FinancialReview, Inspect, Receive, Connect, Send, Disconnect }
