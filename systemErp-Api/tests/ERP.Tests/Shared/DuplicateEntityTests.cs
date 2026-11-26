using ERP.Tests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace ERP.Tests;

/// <summary>
/// الفحص المركزي لمنع التكرار (DuplicateEntityValidator): الاسم العربي والإنجليزي كلٌّ وحده بالتطبيع المعتمد (المسافات والتطويل والتشكيل والحالة،
/// بلا توحيد للهمزات والتاء)، والإنجليزي الفارغ لا يُفحص، واستثناء السجل نفسه، والنطاقات (المنشأة، أب الحساب، أب المركز، عملة البنك)،
/// والموقوف محسوب، ورقم هوية الموظف بدل اسمه، والمسار غير المباشر (اسم الحساب ← كيانه)، والتكرار القديم لا يمنع تعديل بقية الحقول.
/// </summary>
[Collection("api")]
public class DuplicateEntityTests : TestBase
{
    public DuplicateEntityTests(ErpFactory f) : base(f) { }

    private static object CustomerBody(string nameAr, string? nameEn = null, string status = "active", string? code = null)
        => new { code = code ?? "", nameAr, nameEn = nameEn ?? "", phone = "0501234567", city = "الرياض", status };

    private static void Created(Res r) => Assert.True(r.Status == 201, r.Body?.ToJsonString());

    private static void AssertDuplicate(Res r, params string[] parts)
    {
        Assert.True(r.Status == 409, r.Body?.ToJsonString());
        var message = r.Body!["message"].S();
        foreach (var part in parts) Assert.Contains(part, message);
    }

    [Fact]
    public async Task Customer_names_are_normalized_checked_per_language_and_the_message_names_the_duplicate()
    {
        var api = await NewTenantAsync();
        var first = await api.Post("/customers", CustomerBody("مؤسسة الأمل التجارية", "Al Amal Trading", code: "C-A"));
        Assert.Equal(201, first.Status);

        // المسافات الزائدة والتشكيل والتطويل لا تصنع اسماً جديداً
        AssertDuplicate(await api.Post("/customers", CustomerBody("  مؤسسةُ   الأمل  التجاريـــة ")), "مؤسسةُ", "العميل", "C-A");
        // الإنجليزي وحده بلا فرق في الحالة والمسافات
        AssertDuplicate(await api.Post("/customers", CustomerBody("اسم مختلف", "  AL   amal TRADING ")), "AL", "C-A");
        // الهمزات والتاء المربوطة لا تُوحَّد: اسم آخر
        Created(await api.Post("/customers", CustomerBody("موسسة الامل التجاريه")));
        // الإنجليزي الفارغ لا يُفحص
        Created(await api.Post("/customers", CustomerBody("عميل بلا إنجليزي 1")));
        Created(await api.Post("/customers", CustomerBody("عميل بلا إنجليزي 2")));
    }

    [Fact]
    public async Task Update_excludes_the_record_itself_and_rejects_taking_another_records_name()
    {
        var api = await NewTenantAsync();
        var a = (await api.Post("/customers", CustomerBody("عميل أ", "Customer A"))).Data!;
        var b = (await api.Post("/customers", CustomerBody("عميل ب", "Customer B"))).Data!;

        var same = await api.Put($"/customers/{a["id"].S()}", new { code = a["code"].S(), nameAr = "عميل أ", nameEn = "Customer A", phone = "0509999999", city = "جدة", status = "active" });
        Assert.True(same.Status == 200, same.Body?.ToJsonString());

        AssertDuplicate(await api.Put($"/customers/{b["id"].S()}", new { code = b["code"].S(), nameAr = "عميل  أ", nameEn = "Customer B", phone = "0501234567", city = "الرياض", status = "active" }),
            "عميل", a["code"].S());
        Assert.Equal("عميل ب", (await api.Get($"/customers/{b["id"].S()}")).Data!["nameAr"].S());
    }

    [Fact]
    public async Task Inactive_records_count_and_other_tenants_and_entity_types_do_not()
    {
        var api = await NewTenantAsync();
        var other = await NewTenantAsync("منشأة أخرى");
        Created(await api.Post("/suppliers", new { nameAr = "مورد موقوف", phone = "0501112222", city = "جدة", status = "inactive", vatNumber = Client.NewVat() }));

        AssertDuplicate(await api.Post("/suppliers", new { nameAr = "مورد موقوف", phone = "0501112223", city = "جدة", status = "active", vatNumber = Client.NewVat() }), "المورد");
        // منشأة أخرى، ونوع كيان آخر (عميل بالاسم نفسه وحسابه تحت أب آخر)
        Created(await other.Post("/suppliers", new { nameAr = "مورد موقوف", phone = "0501112222", city = "جدة", status = "active", vatNumber = Client.NewVat() }));
        Created(await api.Post("/customers", CustomerBody("مورد موقوف")));
    }

    [Fact]
    public async Task Accounts_are_unique_within_their_parent_and_cost_centers_within_theirs()
    {
        var api = await NewTenantAsync();
        Created(await api.Post("/accounts", new { code = "5181", nameAr = "مصروف مكرر", nameEn = "Dup expense", type = "expense", parentCode = "51", isDebitNature = true }));
        AssertDuplicate(await api.Post("/accounts", new { code = "5182", nameAr = "مصروف  مكرر", nameEn = "", type = "expense", parentCode = "51", isDebitNature = true }), "الحساب", "5181");
        Created(await api.Post("/accounts", new { code = "5281", nameAr = "مصروف مكرر", nameEn = "Dup expense", type = "expense", parentCode = "52", isDebitNature = true }));

        var root = (await api.Post("/costcenters", new { code = "", nameAr = "مركز رئيسي", nameEn = "", isActive = true })).Data!;
        var root2 = (await api.Post("/costcenters", new { code = "", nameAr = "مركز رئيسي 2", nameEn = "", isActive = true })).Data!;
        Created(await api.Post("/costcenters", new { code = "", nameAr = "مشروع", nameEn = "", isActive = true, parentId = root["id"].S() }));
        Created(await api.Post("/costcenters", new { code = "", nameAr = "مشروع", nameEn = "", isActive = true, parentId = root2["id"].S() }));
        AssertDuplicate(await api.Post("/costcenters", new { code = "", nameAr = "مشروع", nameEn = "", isActive = false, parentId = root["id"].S() }), "مركز التكلفة");
        AssertDuplicate(await api.Post("/costcenters", new { code = "", nameAr = "مركز رئيسي", nameEn = "", isActive = true }), "مركز التكلفة", root["code"].S());
    }

    [Fact]
    public async Task Banks_are_unique_by_name_and_currency()
    {
        var api = await NewTenantAsync();
        Created(await api.Post("/currencies", new { code = "USD", nameAr = "دولار", nameEn = "US Dollar", symbol = "$", exchangeRate = 3.75 }));
        var sar = await api.Post("/banks", new { nameAr = "مصرف الاختبار", nameEn = "Test Bank", currency = "SAR" });
        Assert.True(sar.Status == 201, sar.Body?.ToJsonString());
        Created(await api.Post("/banks", new { nameAr = "مصرف الاختبار", nameEn = "Test Bank", currency = "USD" }));
        AssertDuplicate(await api.Post("/banks", new { nameAr = "مصرف الاختبار", nameEn = "", currency = "SAR" }), "البنك", sar.Data!["code"].S());
    }

    [Fact]
    public async Task Employees_are_unique_by_id_number_not_by_name()
    {
        var api = await NewTenantWithModulesAsync("hr");
        object Employee(string code, string? nationalId) => new { code, nameAr = "محمد أحمد", hireDate = "2024-01-01", basicSalary = 5000, nationalId };
        Created(await api.Post("/employees", Employee("E1", "1012345678")));
        Created(await api.Post("/employees", Employee("E2", "1098765432"))); // الاسم نفسه مقبول
        AssertDuplicate(await api.Post("/employees", Employee("E3", " 1012 345678 ")), "رقم الهوية", "E1");
        Created(await api.Post("/employees", Employee("E4", null)));  // بلا هوية: لا فحص
        Created(await api.Post("/employees", Employee("E5", null)));
    }

    [Fact]
    public async Task Payment_methods_warehouses_and_branches_reject_duplicate_names()
    {
        var api = await NewTenantAsync();
        Created(await api.Post("/paymentmethods", new { code = "TR1", nameAr = "تحويل خاص", nameEn = "Special", type = "bank", linkedAccountCode = "1101", status = "active" }));
        AssertDuplicate(await api.Post("/paymentmethods", new { code = "TR2", nameAr = "تحويل خاص", nameEn = "Other", type = "bank", linkedAccountCode = "1101", status = "active" }), "طريقة الدفع", "TR1");

        // الموقع أولاً: بفرع واحد يُلحق به تلقائياً
        Created(await api.Post("/warehouses", new { code = "W1", nameAr = "موقع الشمال", nameEn = "North site", location = "جدة", status = "active" }));
        AssertDuplicate(await api.Post("/warehouses", new { code = "W2", nameAr = "موقع آخر", nameEn = "north SITE", location = "جدة", status = "active" }), "الموقع", "W1");

        Created(await api.Post("/branches", new { code = "B1", nameAr = "فرع الشمال", nameEn = "North", status = "active" }));
        AssertDuplicate(await api.Post("/branches", new { code = "B2", nameAr = "فرع الشمال", nameEn = "", status = "active" }), "الفرع", "B1");
    }

    [Fact]
    public async Task Renaming_a_party_account_to_another_parties_name_is_rejected_through_the_party_rule()
    {
        var api = await NewTenantAsync();
        Created(await api.Post("/customers", CustomerBody("عميل أول")));
        var second = (await api.Post("/customers", CustomerBody("عميل ثان"))).Data!;
        var account = (await api.Get($"/accounts")).Data!["items"]!.AsArray().First(a => a!["code"].S() == second["accountCode"].S())!;
        var r = await api.Put($"/accounts/{account["id"].S()}", new
        {
            code = account["code"].S(), nameAr = "عميل أول", nameEn = "", type = account["type"].S(), parentCode = account["parentCode"].S(),
            isDebitNature = true,
        });
        AssertDuplicate(r, "عميل أول", "العميل");
        Assert.Equal("عميل ثان", (await api.Get($"/customers/{second["id"].S()}")).Data!["nameAr"].S());
    }

    [Fact]
    public async Task Legacy_duplicates_do_not_block_editing_other_fields()
    {
        var api = await NewTenantAsync();
        var a = (await api.Post("/customers", CustomerBody("قديم مكرر"))).Data!;
        var b = (await api.Post("/customers", CustomerBody("قديم آخر"))).Data!;
        // تكرار سابق للفحص (بيانات قائمة)
        await using (var conn = new SqlConnection(Factory.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("UPDATE Customer SET NameAr = N'قديم مكرر' WHERE Id = @id", conn);
            cmd.Parameters.AddWithValue("@id", Guid.Parse(b["id"].S()));
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        var edit = await api.Put($"/customers/{b["id"].S()}", new { code = b["code"].S(), nameAr = "قديم مكرر", nameEn = "", phone = "0507777777", city = "الدمام", status = "active" });
        Assert.True(edit.Status == 200, edit.Body?.ToJsonString());
        Assert.Equal("الدمام", (await api.Get($"/customers/{b["id"].S()}")).Data!["city"].S());
        Assert.Equal("قديم مكرر", (await api.Get($"/customers/{a["id"].S()}")).Data!["nameAr"].S());
    }
}
