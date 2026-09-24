using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IAgreementService : ICrudService<AgreementDto, CreateAgreementDto, UpdateAgreementDto> { }
