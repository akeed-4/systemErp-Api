using System.Text.RegularExpressions;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public static class CarVat
{
    /// <summary>نسخة طبق الأصل من calculateVat في الواجهة (نمط margin_scheme يُعامل كالمعفى كما هناك).</summary>
    public static CarVatResult Calculate(decimal costPrice, decimal sellingPrice, VatMode mode)
    {
        var margin = Math.Max(0, sellingPrice - costPrice);
        switch (mode)
        {
            case VatMode.Standard_15:
                var std = DocumentPricing.Round(sellingPrice * 0.15m);
                return new CarVatResult(margin, std, sellingPrice + std, 0);
            case VatMode.ProfitMargin_15:
                var onMargin = DocumentPricing.Round(margin * 0.15m);
                return new CarVatResult(margin, onMargin, sellingPrice + onMargin, onMargin);
            default:
                return new CarVatResult(margin, 0, sellingPrice, 0);
        }
    }
}
