namespace ERP.Core.DTOs.Shared;

/// <summary>إعلان المنصة كما يراه مديرها، وكما يصل للمنشأة (بلا حقول الاستهداف).</summary>
public class PlatformAnnouncementDto
{
    public Guid Id { get; set; }
    public string TitleAr { get; set; } = string.Empty;
    public string? TitleEn { get; set; }
    public string BodyAr { get; set; } = string.Empty;
    public string? BodyEn { get; set; }
    public string? BadgeAr { get; set; }
    public string? BadgeEn { get; set; }
    public string? CtaLabelAr { get; set; }
    public string? CtaLabelEn { get; set; }
    public string? CtaUrl { get; set; }
    public string Icon { get; set; } = "campaign";
    public string Style { get; set; } = "promo";
    public string Display { get; set; } = "popup";
    public string Audience { get; set; } = "all";
    public List<string> PlanIds { get; set; } = new();
    public List<Guid> TenantIds { get; set; } = new();
    public string Frequency { get; set; } = "once";
    public int Priority { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SavePlatformAnnouncementRequestDto
{
    public string TitleAr { get; set; } = string.Empty;
    public string? TitleEn { get; set; }
    public string BodyAr { get; set; } = string.Empty;
    public string? BodyEn { get; set; }
    public string? BadgeAr { get; set; }
    public string? BadgeEn { get; set; }
    public string? CtaLabelAr { get; set; }
    public string? CtaLabelEn { get; set; }
    public string? CtaUrl { get; set; }
    public string? Icon { get; set; }
    public string Style { get; set; } = "promo";
    public string Display { get; set; } = "popup";
    public string Audience { get; set; } = "all";
    /// <summary>فارغة = كل الباقات.</summary>
    public List<string> PlanIds { get; set; } = new();
    /// <summary>فارغة = كل المنشآت المطابقة.</summary>
    public List<Guid> TenantIds { get; set; } = new();
    public string Frequency { get; set; } = "once";
    public int Priority { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
}
