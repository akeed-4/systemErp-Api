namespace ERP.Core.DTOs.Accounting;

/// <summary>سبب عدم الخضوع للنسبة الأساسية برمز هيئة الزكاة (VATEX) والتصنيف الضريبي الذي ينطبق عليه.</summary>
public record VatExemptionReasonDto(string Code, VatCategory Category, string NameAr, string NameEn, bool IsExport = false);
