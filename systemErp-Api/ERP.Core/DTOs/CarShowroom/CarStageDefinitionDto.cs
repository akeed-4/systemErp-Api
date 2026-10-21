namespace ERP.Core.DTOs.CarShowroom;

/// <summary>مرحلة في دورة الشراء أو عقود البيع بالشكل الذي تعرضه الواجهة (StageDefinition).</summary>
public sealed record CarStageDefinitionDto(
    string Id,
    int StepNumber,
    string NameAr,
    string NameEn,
    string Description,
    string DescriptionEn,
    string Icon,
    string Color);
