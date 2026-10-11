using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.POS;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.POS;

public interface IPosOfferService : ICrudService<PosOfferDto, CreatePosOfferDto, UpdatePosOfferDto> { }
