using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الدليل الذكي: حساب فرعي جديد تحت العملاء أو الموردين أو البنوك (بسلسلة الآباء الفعلية، ومنها مجموعة فرعية) يُنشئ كيانه بالاسم نفسه
/// في المعاملة نفسها مربوطاً بالمعرّف، فيُستخدم مباشرة في الفواتير والسندات. «حساب عام» صراحةً والحساب الرئيسي لا يُنشئان كياناً،
/// وتعديل اسم الحساب يغيّر اسم كيانه، وإعادة المحاولة لا تكرر، والخطأ لا يترك حساباً ولا كياناً.
/// </summary>
[Collection("api")]
public class ChartAutoPartyTests : TestBase
{
    public ChartAutoPartyTests(ErpFactory f) : base(f) { }

    private static object Account(string code, string name, string parent, string type = "asset", bool? isGroup = null, string? linked = null,
        string? phone = null, string? vat = null)
        => new
        {
            code, nameAr = name, nameEn = name, type, parentCode = parent, isDebitNature = type is "asset" or "expense", isGroup,
            linkedEntityType = linked, entityPhone = phone, entityVatNumber = vat,
        };

    private static IEnumerable<JsonNode> Items(Res r)
        => (r.Data is JsonArray a ? a : r.Data!["items"]!.AsArray()).Select(x => x!);

    private static async Task<JsonNode?> PartyAsync(Client api, string path, string accountCode)
        => Items(await api.Get($"/{path}?pageSize=500")).FirstOrDefault(p => p["accountCode"].S() == accountCode);

    [Fact]
    public async Task An_account_under_customers_creates_a_linked_customer_ready_for_sales_invoices()
    {
        var api = await NewTenantAsync();
        var created = await api.Post("/accounts", Account("112901", "مؤسسة النور", "112", phone: "0500000009"));
        Assert.True(created.Status == 201, created.Body?.ToJsonString());
        Assert.Equal("customer", created.Data!["linkedEntityType"].S().ToLowerInvariant());

        var customer = await PartyAsync(api, "customers", "112901");
        Assert.NotNull(customer);
        Assert.Equal("مؤسسة النور", customer!["nameAr"].S());
        Assert.Equal("0500000009", customer["phone"].S());
        Assert.Equal(created.Data["linkedEntityId"].S(), customer["id"].S());

        var sale = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "credit", partyId = customer["id"].G(),
            items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } },
        });
        Assert.True(sale.Status is 200 or 201, sale.Body?.ToJsonString());
        Assert.Equal(115m, (await api.Get("/accounts/ByCode/112901")).Data!["balance"].D());
    }

    [Fact]
    public async Task An_account_under_suppliers_creates_a_supplier_ready_for_purchase_invoices()
    {
        var api = await NewTenantAsync();
        var created = await api.Post("/accounts", Account("211901", "مورد الدليل", "211", type: "liability"));
        Assert.True(created.Status == 201, created.Body?.ToJsonString());
        var supplier = await PartyAsync(api, "suppliers", "211901");
        Assert.NotNull(supplier);

        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier!["id"].G(),
            items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } },
        });
        Assert.True(purchase.Status is 200 or 201, purchase.Body?.ToJsonString());
        Assert.Equal(115m, (await api.Get($"/suppliers/{supplier["id"].S()}")).Data!["currentBalance"].D());
    }

    [Fact]
    public async Task An_account_under_banks_creates_a_bank_with_the_account_currency()
    {
        var api = await NewTenantAsync();
        const string banksGroup = "110"; // البنوك (مجموعة النظام بعد فصلها عن الصناديق)
        var created = await api.Post("/accounts", Account("", "بنك الدليل", banksGroup));
        Assert.True(created.Status == 201, created.Body?.ToJsonString());
        var code = created.Data!["code"].S();
        var bank = await PartyAsync(api, "banks", code);
        Assert.NotNull(bank);
        Assert.Equal("بنك الدليل", bank!["nameAr"].S());
        Assert.False(string.IsNullOrEmpty(bank["currency"].S()));
    }

    [Fact]
    public async Task General_opt_out_and_group_accounts_create_no_entity_but_children_of_a_subgroup_do()
    {
        var api = await NewTenantAsync();
        var customersBefore = Items(await api.Get("/customers?pageSize=500")).Count();

        Assert.Equal(201, (await api.Post("/accounts", Account("112902", "عملاء متنوعون", "112", linked: "general"))).Status);
        Assert.Equal(201, (await api.Post("/accounts", Account("1129", "عملاء الوسطى", "112", isGroup: true))).Status);
        Assert.Equal(customersBefore, Items(await api.Get("/customers?pageSize=500")).Count());

        Assert.Equal(201, (await api.Post("/accounts", Account("11291", "عميل الوسطى", "1129"))).Status);
        Assert.NotNull(await PartyAsync(api, "customers", "11291"));
        // حساب خارج مجموعات الكيانات يبقى عاماً
        Assert.Equal(201, (await api.Post("/accounts", Account("5299", "مصروف", "52", type: "expense"))).Status);
        Assert.Equal(customersBefore + 1, Items(await api.Get("/customers?pageSize=500")).Count());
    }

    [Fact]
    public async Task Renaming_the_account_renames_its_entity_and_retries_or_errors_never_duplicate()
    {
        var api = await NewTenantAsync();
        var created = await api.Post("/accounts", Account("112903", "اسم أول", "112"));
        Assert.Equal(201, created.Status);
        var account = (await api.Get("/accounts/ByCode/112903")).Data!.AsObject();
        account["nameAr"] = "اسم معدّل";
        account["nameEn"] = "Renamed";
        var saved = await api.Put($"/accounts/{account["id"].S()}", account);
        Assert.True(saved.Status == 200, saved.Body?.ToJsonString());
        var customer = await PartyAsync(api, "customers", "112903");
        Assert.Equal("اسم معدّل", customer!["nameAr"].S());
        Assert.Equal("Renamed", customer["nameEn"].S());

        // إعادة الإرسال بالرمز نفسه: مرفوضة ولا كيان ثانٍ
        Assert.Equal(409, (await api.Post("/accounts", Account("112903", "اسم أول", "112"))).Status);
        Assert.Single(Items(await api.Get("/customers?pageSize=500")), c => c["accountCode"].S() == "112903");

        // رقم ضريبي غير صالح: لا حساب ولا عميل (معاملة واحدة)
        Assert.Equal(400, (await api.Post("/accounts", Account("112904", "خطأ", "112", vat: "123"))).Status);
        Assert.Equal(404, (await api.Get("/accounts/ByCode/112904")).Status);
        Assert.Null(await PartyAsync(api, "customers", "112904"));
    }
}
