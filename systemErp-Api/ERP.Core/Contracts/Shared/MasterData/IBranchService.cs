using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>فروع النشاط: المستوى الأوسط بين النشاط والمواقع.</summary>
public interface IBranchService : ICrudService<BranchDto, CreateBranchDto, UpdateBranchDto>
{
}
