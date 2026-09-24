using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface ICarYearService : ICrudService<CarYearModelDto, CreateCarYearModelDto, UpdateCarYearModelDto> { }
