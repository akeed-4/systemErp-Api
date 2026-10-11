namespace ERP.Core.DTOs.Shared;

/// <summary>نتيجة فحص السياسات: هل يلزم اعتماد قبل تنفيذ العملية؟</summary>
public class ApprovalCheckResultDto
{
    public bool ApprovalRequired { get; set; }
    public ApprovalRequestDto? Request { get; set; }
}
