namespace ERP.Core.DTOs.Accounting.Contracts;

public sealed record ContractStageDefinitionDto(
    string Id,
    string WorkflowType,
    int Number,
    string NameAr,
    string NameEn,
    string DescAr,
    string DescEn,
    string Icon,
    string BadgeColor,
    string HeaderColor,
    /// <summary>الإجراءات المسموحة والعقد في المرحلة، وما يلزم للانتقال للتالية (رموز الصلاحيات).</summary>
    IReadOnlyList<string>? AllowedActions = null,
    IReadOnlyList<string>? AdvanceActions = null,
    /// <summary>الاسم القصير (كلمة) للبطاقة، والطور (pre_signing/execution/closing/side)، والمدة المستهدفة بالأيام.</summary>
    string? ShortNameAr = null,
    string? ShortNameEn = null,
    string? Phase = null,
    int? SlaDays = null);

public sealed record ContractStageCatalogDto(
    IReadOnlyList<ContractStageDefinitionDto> Stages,
    IReadOnlyList<ContractStageDefinitionDto> NormalPurchasingStages);