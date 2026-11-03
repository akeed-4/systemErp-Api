namespace ERP.Core.DTOs.Shared;

/// <summary>فهرس المستندات الخاضعة للاعتماد مجمّعاً بالوحدات، كما تعرضه شاشة تعريف السياسة.</summary>
public class ApprovalDocumentCatalogDto
{
    public List<ApprovalModuleDto> Modules { get; set; } = new();
    public List<ApprovalActionDto> Actions { get; set; } = new();
}

public class ApprovalModuleDto
{
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public List<ApprovalDocumentTypeDto> Documents { get; set; } = new();
}

public class ApprovalDocumentTypeDto
{
    public string Key { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public List<string> Actions { get; set; } = new();
}

public class ApprovalActionDto
{
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
}

/// <summary>ما يراه المعتمِد من عملية محجوزة: ملخص موحّد لأي مستند (رأس، حقول، بنود، مبلغ).</summary>
public class ApprovalDocumentSnapshotDto
{
    public string DocumentType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Number { get; set; }
    public DateTime? Date { get; set; }
    public string? PartyName { get; set; }
    public string? Notes { get; set; }
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public List<ApprovalSnapshotFieldDto> Fields { get; set; } = new();
    public List<ApprovalSnapshotLineDto> Lines { get; set; } = new();
}

public class ApprovalSnapshotFieldDto
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class ApprovalSnapshotLineDto
{
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Debit { get; set; }
    public decimal? Credit { get; set; }
    public decimal? Total { get; set; }
}
