namespace ERP.Core.DTOs.Shared;

public record NotificationSummaryDto(int UnreadCount, DateTime? LatestAt);
