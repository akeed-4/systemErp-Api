namespace ERP.Core.DTOs.Shared;

/// <summary>عملية محجوزة بطلب اعتماد: معالجها وعمليتها ومستندها وبياناتها ومقدّمها.</summary>
public sealed record ApprovalDeferredOperation(string Handler, string Action, string DocumentType, Guid DocumentId, string? Payload, Guid RequesterUserId);
