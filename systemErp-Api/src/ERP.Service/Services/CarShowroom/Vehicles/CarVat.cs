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
    /// نسخة طبق الأصل من calculateVat في الواجهة.
    /// هامش الربح (المادة 49 من اللائحة التنفيذية): الضريبة مضمَّنة في الهامش = الهامش × 15/115، ولا تُضاف على سعر العميل؛
    /// البيع بخسارة لا ضريبة عليه.
    /// </summary>
    public static CarVatResult Calculate(decimal costPrice, decimal sellingPrice, VatMode mode)
    {
        var margin = Math.Max(0, sellingPrice - costPrice);
        if (mode == VatMode.Standard_15)
        {
            var std = DocumentPricing.Round(sellingPrice * 0.15m);
            return new CarVatResult(margin, std, sellingPrice + std, 0);
        }
        if (IsMarginScheme(mode))
        {
            var onMargin = DocumentPricing.Round(margin * 15m / 115m);
            return new CarVatResult(margin, onMargin, sellingPrice, onMargin);
        }
        return new CarVatResult(margin, 0, sellingPrice, 0);
    }
}
