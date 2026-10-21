using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;

namespace ERP.Service.Services.CarShowroom;

/// <summary>قراءة كتالوج مراحل دورتي الشراء وعقود البيع (CarStageDefinition) مرتبة برقم المرحلة.</summary>
internal static class CarStageCatalog
{
    public static Task<List<CarStageDefinitionDto>> ListAsync(ErpDbContext db, string workflowType, CancellationToken ct)
        => db.Set<CarStageDefinition>()
            .AsNoTracking()
            .Where(s => s.IsActive && s.WorkflowType == workflowType)
            .OrderBy(s => s.Number)
            .Select(s => new CarStageDefinitionDto(s.Id, s.Number, s.NameAr, s.NameEn, s.DescAr, s.DescEn, s.Icon, s.Color))
            .ToListAsync(ct);
}
