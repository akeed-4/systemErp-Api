using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الحساب المرتبط بعميل/مورد/بنك: حذف الكيان يحذف حسابه معه، وحذف الحساب من الدليل يحذف كيانه، ما لم توجد قيود أو سجلات مانعة
/// فيُرفض كله. تعديل البيانات العادية مسموح دائماً، وتغيير الحساب المرتبط أو رمزه مسموح بلا تاريخ محاسبي فقط ولا يقطع الارتباط،
/// ولا يُستبدل بحساب قائم آخر. الفشل في منتصف العملية يُرجع كل شيء.
/// </summary>
[Collection("api")]
public class LinkedPartyAccountTests : TestBase
{
    public LinkedPartyAccountTests(ErpFactory f) : base(f) { }

    private static async Task<int> AccountStatusAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Status;

    private static async Task<JsonObject> GetAsync(Client api, string path) => (await api.Get(path)).Data!.AsObject();

    private static async Task<(string Id, string Code)> BankAsync(Client api, decimal opening = 0)
    {
        var bank = await api.Post("/banks", new { nameAr = "بنك الاختبار", nameEn = "Test Bank", accountNumber = "123456", iban = "SA0380000000608010167519", openingBalance = opening, status = "active" });
        Assert.True(bank.Status is 200 or 201, bank.Body?.ToJsonString());
        return (bank.Data!["id"].S(), bank.Data["accountCode"].S());
    }

    // ---------- الحذف ----------
    [Fact]
    public async Task Deleting_parties_without_activity_deletes_their_accounts_with_them()
    {
        var api = await NewTenantAsync();
        var (customer, customerCode) = await SeedCustomerAsync(api);
        var (supplier, supplierCode) = await SeedSupplierAsync(api);
        var (bank, bankCode) = await BankAsync(api);
        foreach (var code in new[] { customerCode, supplierCode, bankCode }) Assert.Equal(200, await AccountStatusAsync(api, code));

        Assert.Equal(200, (await api.Delete($"/customers/{customer}")).Status);
        Assert.Equal(200, (await api.Delete($"/suppliers/{supplier}")).Status);
        Assert.Equal(200, (await api.Delete($"/banks/{bank}")).Status);

        Assert.Equal(404, (await api.Get($"/customers/{customer}")).Status);
        Assert.Equal(404, (await api.Get($"/suppliers/{supplier}")).Status);
        Assert.Equal(404, (await api.Get($"/banks/{bank}")).Status);
        foreach (var code in new[] { customerCode, supplierCode, bankCode }) Assert.Equal(404, await AccountStatusAsync(api, code));
    }

    [Fact]
    public async Task Parties_with_entries_or_linked_records_are_not_deleted_and_nothing_is_removed()
    {
        var api = await NewTenantAsync();
        var (customer, customerCode) = await SeedCustomerAsync(api);
        var receipt = await api.Post("/vouchers", new { type = "receipt", amount = 100, partyName = "عميل", partyAccountCode = customerCode, treasuryAccountCode = "1111", paymentMethod = "cash" });
        Assert.True(receipt.Success, receipt.Body?.ToJsonString());
        Assert.Equal(409, (await api.Delete($"/customers/{customer}")).Status);
        Assert.Equal(200, (await api.Get($"/customers/{customer}")).Status);
        Assert.Equal(200, await AccountStatusAsync(api, customerCode));

        // بنك برصيد افتتاحي: عليه قيد
        var (bank, bankCode) = await BankAsync(api, opening: 1000);
        Assert.Equal(409, (await api.Delete($"/banks/{bank}")).Status);
        Assert.Equal(200, await AccountStatusAsync(api, bankCode));

        // مورد بلا قيود لكن مسودة فاتورة شراء تشير إليه: يُرفض مع ذكر السبب، ويبقى حسابه
        var product = await SeedProductAsync(api, stock: 0);
        var (supplier, supplierCode) = await SeedSupplierAsync(api, "مورد مسودة");
        var draft = await api.Post("/purchase-invoices", new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "draft",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 10, vatRate = 15 } },
        });
        Assert.Equal(201, draft.Status);
        Assert.Equal("draft", draft.Data!["status"].S());
        var refused = await api.Delete($"/suppliers/{supplier}");
        Assert.Equal(409, refused.Status);
        Assert.Contains("Invoice", refused.Body?.ToJsonString());
        Assert.Equal(200, (await api.Get($"/suppliers/{supplier}")).Status);
        Assert.Equal(200, await AccountStatusAsync(api, supplierCode));
    }

    [Fact]
    public async Task Deleting_a_linked_account_from_the_chart_deletes_its_party_or_refuses_both()
    {
        var api = await NewTenantAsync();
        var (supplier, supplierCode) = await SeedSupplierAsync(api);
        var account = await GetAsync(api, $"/accounts/ByCode/{supplierCode}");
        Assert.Equal(200, (await api.Delete($"/accounts/{account["id"].S()}")).Status);
        Assert.Equal(404, (await api.Get($"/suppliers/{supplier}")).Status);

        var (bank, bankCode) = await BankAsync(api, opening: 500);
        var bankAccount = await GetAsync(api, $"/accounts/ByCode/{bankCode}");
        Assert.Equal(409, (await api.Delete($"/accounts/{bankAccount["id"].S()}")).Status);
        Assert.Equal(200, (await api.Get($"/banks/{bank}")).Status);
        Assert.Equal(200, await AccountStatusAsync(api, bankCode));
    }

    // ---------- التعديل ----------
    [Fact]
    public async Task Ordinary_edits_are_allowed_with_activity_and_the_account_follows_the_name()
    {
        var api = await NewTenantAsync();
        var (customer, code) = await SeedCustomerAsync(api);
        Assert.True((await api.Post("/vouchers", new { type = "receipt", amount = 50, partyName = "عميل", partyAccountCode = code, treasuryAccountCode = "1111", paymentMethod = "cash" })).Success);

        var body = await GetAsync(api, $"/customers/{customer}");
        body["nameAr"] = "عميل معدّل"; body["phone"] = "0555555555";
        Assert.Equal(200, (await api.Put($"/customers/{customer}", body)).Status);
        var after = await GetAsync(api, $"/customers/{customer}");
        Assert.Equal(code, after["accountCode"].S());
        Assert.Equal("عميل معدّل", (await GetAsync(api, $"/accounts/ByCode/{code}"))["nameAr"].S());
    }

    [Fact]
    public async Task Linked_account_is_recoded_only_without_history_and_is_never_swapped_for_another_account()
    {
        var api = await NewTenantAsync();
        var (customer, code) = await SeedCustomerAsync(api);
        var (_, otherCode) = await SeedCustomerAsync(api, name: "عميل آخر");
        var body = await GetAsync(api, $"/customers/{customer}");

        // استبدال بحساب قائم آخر (عميل آخر، أو الصندوق): مرفوض ولا يتغيّر شيء
        foreach (var taken in new[] { otherCode, "1111" })
        {
            body["accountCode"] = taken;
            Assert.True((await api.Put($"/customers/{customer}", body)).Status is 400 or 409);
            Assert.Equal(code, (await GetAsync(api, $"/customers/{customer}"))["accountCode"].S());
        }

        // رمز جديد خارج مجموعة العملاء: مرفوض
        body["accountCode"] = "211999";
        Assert.Equal(400, (await api.Put($"/customers/{customer}", body)).Status);

        // بلا تاريخ: يُعاد ترقيم حسابه نفسه ويبقى مرتبطاً به
        body["accountCode"] = "112777";
        Assert.Equal(200, (await api.Put($"/customers/{customer}", body)).Status);
        Assert.Equal("112777", (await GetAsync(api, $"/customers/{customer}"))["accountCode"].S());
        Assert.Equal(404, await AccountStatusAsync(api, code));
        var recoded = await GetAsync(api, $"/accounts/ByCode/112777");
        Assert.Equal(customer.ToString(), recoded["linkedEntityId"].S());

        // بعد أول قيد: تغيير الحساب مرفوض
        Assert.True((await api.Post("/vouchers", new { type = "receipt", amount = 10, partyName = "عميل", partyAccountCode = "112777", treasuryAccountCode = "1111", paymentMethod = "cash" })).Success);
        body = await GetAsync(api, $"/customers/{customer}");
        body["accountCode"] = "112778";
        Assert.Equal(409, (await api.Put($"/customers/{customer}", body)).Status);
        Assert.Equal("112777", (await GetAsync(api, $"/customers/{customer}"))["accountCode"].S());
        Assert.Equal(404, await AccountStatusAsync(api, "112778"));
    }

    [Fact]
    public async Task Recoding_a_linked_account_from_the_chart_follows_on_the_party_and_respects_history()
    {
        var api = await NewTenantAsync();
        var (supplier, code) = await SeedSupplierAsync(api);
        var account = await GetAsync(api, $"/accounts/ByCode/{code}");

        // خارج مجموعة الموردين: مرفوض
        var outside = (JsonObject)account.DeepClone(); outside["code"] = "112555"; outside["parentCode"] = "112";
        Assert.True((await api.Put($"/accounts/{account["id"].S()}", outside)).Status is 400 or 409);

        account["code"] = "211555";
        Assert.Equal(200, (await api.Put($"/accounts/{account["id"].S()}", account)).Status);
        Assert.Equal("211555", (await GetAsync(api, $"/suppliers/{supplier}"))["accountCode"].S());

        Assert.True((await api.Post("/vouchers", new { type = "payment", amount = 20, partyName = "مورد", partyAccountCode = "211555", treasuryAccountCode = "1111", paymentMethod = "cash" })).Success);
        account["code"] = "211556";
        Assert.Equal(409, (await api.Put($"/accounts/{account["id"].S()}", account)).Status);
        Assert.Equal("211555", (await GetAsync(api, $"/suppliers/{supplier}"))["accountCode"].S());
    }

    [Fact]
    public async Task A_party_with_an_account_cannot_be_linked_to_a_second_account_from_the_chart()
    {
        var api = await NewTenantAsync();
        var (customer, code) = await SeedCustomerAsync(api);
        var second = await api.Post("/accounts", new
        {
            code = "112901", nameAr = "حساب ثانٍ", type = "asset", parentCode = "112", isDebitNature = true,
            linkedEntityType = "customer", linkedEntityId = customer,
        });
        Assert.Equal(409, second.Status);
        Assert.Equal(404, await AccountStatusAsync(api, "112901"));
        Assert.Equal(code, (await GetAsync(api, $"/customers/{customer}"))["accountCode"].S());
    }

    [Fact]
    public async Task A_failure_after_the_account_change_rolls_back_the_whole_edit()
    {
        var api = await NewTenantAsync();
        var (customer, code) = await SeedCustomerAsync(api);
        // الفترة مقفلة حتى اليوم: قيد الرصيد الافتتاحي (بتاريخ اليوم) يفشل بعد حفظ الرمز الجديد داخل المعاملة
        Assert.Equal(200, (await api.Put("/fiscalperiods/lock", new { lockedThrough = DateTime.UtcNow.AddHours(3).Date })).Status);

        var body = await GetAsync(api, $"/customers/{customer}");
        body["accountCode"] = "112888"; body["openingBalance"] = 500; body["nameAr"] = "اسم لا يُحفظ";
        Assert.True((await api.Put($"/customers/{customer}", body)).Status is >= 400 and < 500);

        var after = await GetAsync(api, $"/customers/{customer}");
        Assert.Equal(code, after["accountCode"].S());
        Assert.NotEqual("اسم لا يُحفظ", after["nameAr"].S());
        Assert.Equal(200, await AccountStatusAsync(api, code));
        Assert.Equal(404, await AccountStatusAsync(api, "112888"));
    }
}
