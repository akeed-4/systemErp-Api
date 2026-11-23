namespace ERP.Core.Models.Shared;

public enum LinkedEntityType
{
    General = 0,
    Customer = 1,
    Supplier = 2,
    Bank = 3,
    /// <summary>حساب مخزون الموقع (المستودع) تحت المخزون 114: يُنشأ للموقع الرئيسي عند تأسيس المنشأة؛ المواقع المضافة تختار حسابها.</summary>
    Warehouse = 4,
    /// <summary>
    /// حساب مخصص لأصل ثابت (مجمع إهلاكه أو مصروف إهلاكه) يُنشأ مع الأصل. الربط الدائم بمعرّف الحساب على الأصل
    /// (<c>FixedAsset.AccumulatedDepreciationAccountId</c> و<c>DepreciationExpenseAccountId</c>)، وهذا الوسم يحمي الحساب من الحذف المنفرد.
    /// </summary>
    FixedAsset = 5
}
