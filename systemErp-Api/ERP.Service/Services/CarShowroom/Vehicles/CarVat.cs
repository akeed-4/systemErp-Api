using System.Text.RegularExpressions;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public static class CarVat
{
    /// <summary>نمطا هامش الربح (profit_margin_15 و margin_scheme) يُعاملان معاملة واحدة.</summary>
    public static bool IsMarginScheme(VatMode mode) => mode is VatMode.ProfitMargin_15 or VatMode.MarginScheme;

    /// <summary>
    /// حساب ضريبة السيارة بنسبة الفئة «خاضع» من إعدادات المعرض (لا نسبة ثابتة في الكود).
    /// هامش الربح (المادة 49 من اللائحة التنفيذية): الضريبة مضمَّنة في الهامش = الهامش × النسبة ÷ (100 + النسبة)، ولا تُضاف على سعر العميل؛
    /// البيع بخسارة لا ضريبة عليه. <paramref name="marginBase"/> أساس الهامش (سعر الشراء أو التكلفة الكلية حسب الإعداد).
    /// </summary>
    public static CarVatResult Calculate(decimal marginBase, decimal sellingPrice, VatMode mode, decimal rate)
    {
        var margin = Math.Max(0, sellingPrice - marginBase);
        if (mode == VatMode.Standard_15)
        {
            var std = DocumentPricing.Round(sellingPrice * rate / 100m);
            return new CarVatResult(margin, std, sellingPrice + std, 0);
        }
        if (IsMarginScheme(mode))
        {
            var onMargin = DocumentPricing.Round(margin * rate / (100m + rate));
            return new CarVatResult(margin, onMargin, sellingPrice, onMargin);
        }
        return new CarVatResult(margin, 0, sellingPrice, 0);
    }
}

/// <summary>
/// الإعداد الضريبي للمعرض كما حدّده محاسب المنشأة: نسبة الفئة «خاضع» وأساس هامش الربح. يُحمَّل من الإعدادات في كل عملية،
/// فلا تُثبَّت قاعدة ضريبية في الكود.
/// </summary>
public sealed record CarTax(decimal Rate, MarginBasis Basis)
{
    public static async Task<CarTax> LoadAsync(ErpDbContext db, CancellationToken ct)
    {
        var settings = await SalesCharges.SettingsAsync(db, ct);
        return new CarTax(settings.StandardVatRate, settings.MarginBasis);
    }

    /// <summary>أساس الهامش الخاضع: سعر الشراء من البائع فقط، أو التكلفة الكلية بعد المصاريف المُرسمَلة.</summary>
    public decimal MarginBase(decimal? purchasePrice, decimal totalCost) => Basis == MarginBasis.TotalCost ? totalCost : purchasePrice ?? 0;

    public CarVatResult Calculate(decimal? purchasePrice, decimal totalCost, decimal sellingPrice, VatMode mode)
        => CarVat.Calculate(MarginBase(purchasePrice, totalCost), sellingPrice, mode, Rate);

    /// <summary>نسبة بند الفاتورة: النسبة الخاضعة للنظام القياسي، وصفر لغيره (ضريبة الهامش تُحمَّل مبلغاً ثابتاً على البند).</summary>
    public decimal RateFor(VatMode mode) => mode == VatMode.Standard_15 ? Rate : 0;
}
