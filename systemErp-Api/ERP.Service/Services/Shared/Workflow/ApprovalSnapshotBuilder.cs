using System.Collections;
using System.Globalization;
using System.Reflection;

namespace ERP.Service.Services.Shared;

/// <summary>
/// ملخص موحّد لأي مستند يراه المعتمِد (رقم، تاريخ، طرف، مبلغ، حقول مميزة، بنود) يُستخرج من DTO المستند بأسماء خصائصه المعتادة،
/// فلا يحتاج كل مستند جديد في سجل الاعتماد إلى كود عرض خاص.
/// </summary>
public static class ApprovalSnapshotBuilder
{
    private static readonly string[] NumberProps =
    {
        "VoucherNumber", "EntryNumber", "QuotationNumber", "OrderNumber", "DeliveryNumber", "ReturnNumber", "RequisitionNumber",
        "TransferNumber", "ContractNumber", "RunNumber", "LoanNumber", "SettlementNumber", "Number", "InvoiceNumber",
    };
    private static readonly string[] DateProps = { "Date", "OrderDate", "RequestDate", "StartDate", "LastWorkingDay" };
    private static readonly string[] PartyProps = { "PartyName", "SupplierName", "BuyerName", "EmployeeName", "CustomerName" };
    private static readonly string[] NotesProps = { "Notes", "Description", "Reason", "ReturnReason" };
    private static readonly string[] AmountProps =
    {
        "GrandTotalWithCharges", "GrandTotal", "TotalValueWithVat", "TotalWithVat", "TotalAmount", "TotalNet", "Net",
        "Amount", "TotalEstimatedCost", "TotalDebit", "ContractValue", "SellingPrice", "Total",
    };
    private static readonly string[] CurrencyProps = { "CurrencyCode", "Currency" };

    /// <summary>حقول مميزة تُعرض للمعتمِد إن وُجدت في المستند (الخاصية ← عنوانها).</summary>
    private static readonly (string Prop, string Label)[] FieldProps =
    {
        ("Title", "العنوان"), ("ContractType", "نوع العقد"), ("Period", "الفترة"), ("Department", "القسم"), ("Priority", "الأولوية"),
        ("WarehouseName", "المستودع"), ("FromWarehouseName", "من مستودع"), ("ToWarehouseName", "إلى مستودع"),
        ("PaymentMethod", "طريقة الدفع"), ("ReferenceNumber", "المرجع"), ("SupplierInvoiceNumber", "فاتورة المورد"),
        ("ProcurementOrderNumber", "أمر الشراء"), ("ExpenseTypeNameAr", "نوع المصروف"), ("DeliveryNumber", "سند التسليم"),
        ("ContractNumber", "العقد"), ("VehicleDescription", "المركبة"), ("Vin", "رقم الهيكل"), ("EmployeeCode", "رقم الموظف"),
        ("Months", "عدد الأشهر"), ("InstallmentAmount", "القسط"), ("EndDate", "تاريخ النهاية"),
        ("RequiredDate", "التاريخ المطلوب"), ("ValidUntil", "صالح حتى"), ("ExpectedDeliveryDate", "التسليم المتوقع"),
    };

    private static readonly string[] LineCollections = { "Items", "Lines" };
    private static readonly string[] LineNameProps = { "ItemName", "AccountName", "EmployeeName", "Description", "Name", "ExpenseTypeNameAr", "VehicleDescription" };
    private static readonly string[] LineTotalProps = { "TotalAfterVat", "LineTotal", "Total", "NetSalary", "Net", "Amount", "TotalCost" };

    public static ApprovalDocumentSnapshotDto Build(string documentType, string action, object? source, Func<object, decimal?>? amount, bool includeNumber)
    {
        var snapshot = new ApprovalDocumentSnapshotDto { DocumentType = documentType, Action = action };
        if (source == null) return snapshot;

        var number = Text(source, NumberProps);
        if (includeNumber) snapshot.Number = number;
        snapshot.Date = Date(source, DateProps);
        snapshot.PartyName = Text(source, PartyProps);
        snapshot.Notes = Text(source, NotesProps);
        snapshot.CurrencyCode = Text(source, CurrencyProps);
        snapshot.Amount = amount?.Invoke(source) ?? Dec(source, AmountProps) ?? 0;

        foreach (var (prop, label) in FieldProps)
        {
            if (NumberProps.Contains(prop) && prop != "DeliveryNumber" && prop != "ContractNumber") continue;
            var value = Format(Prop(source, prop)?.GetValue(source));
            // رقم المستند نفسه لا يتكرر كحقل (سند التسليم في مرتجعه، والعقد في سنده)
            if (!string.IsNullOrWhiteSpace(value) && value != number) snapshot.Fields.Add(new ApprovalSnapshotFieldDto { Label = label, Value = value });
        }

        foreach (var name in LineCollections)
        {
            if (Prop(source, name)?.GetValue(source) is not IEnumerable items || items is string) continue;
            foreach (var item in items)
            {
                if (item == null) continue;
                var lineName = Text(item, LineNameProps) ?? string.Empty;
                var account = Text(item, new[] { "AccountCode" });
                snapshot.Lines.Add(new ApprovalSnapshotLineDto
                {
                    Name = account != null && !lineName.Contains(account) ? $"{account} {lineName}".Trim() : lineName,
                    Unit = Text(item, new[] { "Unit" }),
                    Quantity = Dec(item, new[] { "Quantity" }),
                    UnitPrice = Dec(item, new[] { "UnitPrice", "Price" }),
                    Debit = Dec(item, new[] { "Debit" }),
                    Credit = Dec(item, new[] { "Credit" }),
                    Total = Dec(item, LineTotalProps),
                });
            }
            break;
        }
        return snapshot;
    }

    public static Guid? Id(object? source) => source == null ? null : Prop(source, "Id")?.GetValue(source) as Guid?;

    public static string? Number(object? source) => source == null ? null : Text(source, NumberProps);

    private static PropertyInfo? Prop(object source, string name) => source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

    private static string? Text(object source, IEnumerable<string> names)
    {
        foreach (var n in names)
            if (Prop(source, n)?.GetValue(source) is string s && !string.IsNullOrWhiteSpace(s)) return s.Trim();
        return null;
    }

    private static DateTime? Date(object source, IEnumerable<string> names)
    {
        foreach (var n in names)
            if (Prop(source, n)?.GetValue(source) is DateTime d && d != default) return d;
        return null;
    }

    private static decimal? Dec(object source, IEnumerable<string> names)
    {
        foreach (var n in names)
            switch (Prop(source, n)?.GetValue(source))
            {
                case decimal d: return d;
                case int i: return i;
                case double x: return (decimal)x;
            }
        return null;
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => s.Trim(),
        DateTime d => d == default ? null : d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m == 0 ? null : m.ToString("#,0.##", CultureInfo.InvariantCulture),
        int i => i == 0 ? null : i.ToString(CultureInfo.InvariantCulture),
        bool => null,
        Enum e => e.ToString(),
        _ when value.GetType().IsPrimitive => Convert.ToString(value, CultureInfo.InvariantCulture),
        _ => null,
    };
}
