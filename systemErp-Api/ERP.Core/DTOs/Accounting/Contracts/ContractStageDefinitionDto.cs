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
    string HeaderColor);

public sealed record ContractStageCatalogDto(
    IReadOnlyList<ContractStageDefinitionDto> Stages,
    IReadOnlyList<ContractStageDefinitionDto> NormalPurchasingStages);