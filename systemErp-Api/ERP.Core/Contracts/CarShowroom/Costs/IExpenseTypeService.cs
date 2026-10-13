using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;

namespace ERP.Core.Contracts.CarShowroom;

public interface IExpenseTypeService : ICrudService<ExpenseTypeDto, CreateExpenseTypeDto, UpdateExpenseTypeDto>
{
}
