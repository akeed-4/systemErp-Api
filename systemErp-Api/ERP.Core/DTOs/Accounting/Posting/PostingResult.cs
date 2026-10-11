using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public record PostingResult(Guid JournalEntryId, string EntryNumber);
