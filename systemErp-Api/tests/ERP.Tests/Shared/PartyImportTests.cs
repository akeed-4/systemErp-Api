using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// استيراد العملاء والموردين من Excel: فحص في السرفر قبل الحفظ (الحقول، توحيد الهاتف، الآيبان، التكرار مع المسجّل وداخل الملف)،
/// ثم حفظ السليم وما عليه تنبيه فقط، ورفض صفوف الأخطاء بأسبابها. لا يُحدَّث طرف موجود.
/// </summary>
[Collection("api")]
public class PartyImportTests : TestBase
{
    public PartyImportTests(ErpFactory f) : base(f) { }

    private static object Row(int rowNumber, object data) => new { rowNumber, data };

    private static Dictionary<int, JsonNode> ByRow(JsonNode preview)
        => preview["rows"]!.AsArray().ToDictionary(r => (int)r!["rowNumber"].D(), r => r!);

    private static string[] Reasons(JsonNode row, string kind) => row[kind]!.AsArray().Select(x => x!.S()).ToArray();

    [Fact]
    public async Task Phone_numbers_in_every_common_format_are_normalized_and_bad_ones_are_rejected()
    {
        var api = await NewTenantAsync();
        string[] sameMobile = { "0501234567", "501234567", "+966501234567", "00966501234567", "966501234567", "٠٥٠١٢٣٤٥٦٧", "050-123-4567", "+966 50 123 4567" };
        var rows = sameMobile.Select((p, i) => Row(i + 2, new { nameAr = "عميل " + i, phone = p })).ToList();
        rows.Add(Row(20, new { nameAr = "ثابت", phone = "011 456 7890" }));
        rows.Add(Row(21, new { nameAr = "دولي", phone = "+44 20 7946 0958" }));
        rows.Add(Row(22, new { nameAr = "موحّد", phone = "920012345" }));
        rows.Add(Row(23, new { nameAr = "قصير", phone = "12345" }));
        rows.Add(Row(24, new { nameAr = "حروف", phone = "05012abc67" }));

        var preview = await api.Post("/customers/import/preview", rows);
        Assert.Equal(200, preview.Status);
        var r = ByRow(preview.Data!);
        foreach (var i in Enumerable.Range(2, sameMobile.Length)) Assert.Equal("0501234567", r[i]["phone"].S());
        Assert.Equal("valid", r[2]["status"].S());
        Assert.Equal("warning", r[3]["status"].S()); // الرقم نفسه مكرر في الملف: تنبيه لا خطأ
        Assert.Contains(Reasons(r[3], "warnings"), w => w.Contains("0501234567") && w.Contains('2'));
        Assert.Equal("0114567890", r[20]["phone"].S()); Assert.NotEqual("error", r[20]["status"].S());
        Assert.Equal("+442079460958", r[21]["phone"].S()); Assert.NotEqual("error", r[21]["status"].S());
        Assert.Equal("920012345", r[22]["phone"].S()); Assert.NotEqual("error", r[22]["status"].S());
        Assert.Equal("error", r[23]["status"].S()); Assert.Equal("error", r[24]["status"].S());
    }

    [Fact]
    public async Task Customer_import_previews_without_saving_then_saves_valid_and_warning_rows_and_rejects_errors()
    {
        var api = await NewTenantAsync();
        var existing = await api.Post("/customers", new { code = "C-EX", nameAr = "مؤسسة الأمل التجارية", phone = "0551112222", vatNumber = "300000000000003", crNumber = "1010101010", city = "الرياض", creditLimit = 1000, status = "active" });
        Assert.Equal(201, existing.Status);
        var countBefore = (await api.Get("/customers")).Data!["totalCount"].D();

        var rows = new[]
        {
            Row(2, new { code = "C-1", nameAr = "شركة النور", phone = "0501230001", vatNumber = "300000000000013", city = "الرياض", creditLimit = 5000, openingBalance = 250 }),
            Row(3, new { code = "C-2", nameAr = "بلا هاتف" }),                                                     // الهاتف إلزامي للعميل
            Row(4, new { code = "C-EX", nameAr = "كود مكرر", phone = "0501230004" }),                                // الكود مسجّل
            Row(5, new { code = "C-5", nameAr = "ضريبي مسجّل", phone = "0501230005", vatNumber = "300000000000003" }), // الرقم الضريبي مسجّل
            Row(6, new { code = "C-6", nameAr = "سجل أول", phone = "0501230006", crNumber = "2020202020" }),
            Row(7, new { code = "C-7", nameAr = "سجل مكرر", phone = "0501230007", crNumber = "2020202020" }),         // مكرر في الملف
            Row(8, new { code = "C-8", nameAr = "فرع آخر", phone = "+966 55 111 2222" }),                           // هاتف مسجّل: تنبيه
            Row(9, new { code = "C-9", nameAr = "  موسسة   الامل التجاريه ", phone = "0501230009" }),               // الاسم نفسه بصيغة أخرى: تنبيه
            Row(10, new { code = "C-10", nameAr = "", phone = "0501230010" }),                                      // الاسم إلزامي
            Row(11, new { code = "C-11", nameAr = "ضريبي خطأ", phone = "0501230011", vatNumber = "12345" }),
            Row(12, new { code = "C-12", nameAr = "رصيد سالب للحد", phone = "0501230012", creditLimit = -1 }),
        };

        var preview = await api.Post("/customers/import/preview", rows);
        Assert.Equal(200, preview.Status);
        var p = preview.Data!;
        Assert.Equal(11, p["totalRows"].D());
        Assert.Equal(2, p["validCount"].D()); Assert.Equal(2, p["warningCount"].D()); Assert.Equal(7, p["errorCount"].D());
        var r = ByRow(p);
        Assert.Equal("valid", r[2]["status"].S()); Assert.Equal("valid", r[6]["status"].S());
        Assert.Contains(Reasons(r[4], "errors"), e => e.Contains("C-EX") && e.Contains("مؤسسة الأمل التجارية"));
        Assert.Contains(Reasons(r[5], "errors"), e => e.Contains("300000000000003"));
        Assert.Contains(Reasons(r[7], "errors"), e => e.Contains("2020202020") && e.Contains('6'));
        Assert.Equal("warning", r[8]["status"].S()); Assert.Contains(Reasons(r[8], "warnings"), w => w.Contains("0551112222"));
        Assert.Equal("warning", r[9]["status"].S()); Assert.Contains(Reasons(r[9], "warnings"), w => w.Contains("مؤسسة الأمل التجارية"));
        foreach (var n in new[] { 3, 10, 11, 12 }) Assert.Equal("error", r[n]["status"].S());
        // المعاينة لا تحفظ شيئاً
        Assert.Equal(countBefore, (await api.Get("/customers")).Data!["totalCount"].D());

        var import = await api.Post("/customers/import", rows);
        Assert.Equal(200, import.Status);
        Assert.Equal(4, import.Data!["successCount"].D()); Assert.Equal(7, import.Data["failedCount"].D());
        var results = import.Data["results"]!.AsArray().ToDictionary(x => (int)x!["rowNumber"].D(), x => x!);
        Assert.False(results[4]["success"]!.GetValue<bool>()); Assert.Contains("C-EX", results[4]["error"].S());
        Assert.True(results[8]["success"]!.GetValue<bool>());
        Assert.Equal(countBefore + 4, (await api.Get("/customers")).Data!["totalCount"].D());

        // الهاتف محفوظ موحّداً، والرصيد الافتتاحي قُيِّد، والمسجّل لم يُمسّ
        var saved = (await api.Get($"/customers/{results[8]["id"].S()}")).Data!;
        Assert.Equal("0551112222", saved["phone"].S());
        Assert.Equal(250, (await api.Get($"/customers/{results[2]["id"].S()}")).Data!["openingBalance"].D());
        var untouched = (await api.Get($"/customers/{existing.Data!["id"].S()}")).Data!;
        Assert.Equal("مؤسسة الأمل التجارية", untouched["nameAr"].S()); Assert.Equal(1000, untouched["creditLimit"].D());

        // إعادة الاستيراد نفسه: كل ما حُفظ صار مكرراً بالكود
        var again = await api.Post("/customers/import/preview", rows);
        Assert.Equal(0, again.Data!["validCount"].D() + again.Data["warningCount"].D());
    }

    [Fact]
    public async Task Supplier_import_makes_phone_optional_and_checks_the_iban()
    {
        var api = await NewTenantAsync();
        var rows = new[]
        {
            Row(2, new { nameAr = "مورد بلا هاتف", iban = "SA03 8000 0000 6080 1016 7519" }),
            Row(3, new { nameAr = "آيبان خطأ", iban = "SA0380000000608010167518" }),     // خانتا التحقق لا تطابقان
            Row(4, new { nameAr = "آيبان قصير", iban = "SA038000000060801016" }),
            Row(5, new { nameAr = "هاتف خطأ", phone = "0412" }),
        };
        var preview = await api.Post("/suppliers/import/preview", rows);
        Assert.Equal(200, preview.Status);
        var r = ByRow(preview.Data!);
        Assert.Equal("valid", r[2]["status"].S());
        Assert.Equal("error", r[3]["status"].S()); Assert.Equal("error", r[4]["status"].S()); Assert.Equal("error", r[5]["status"].S());

        var import = await api.Post("/suppliers/import", rows);
        Assert.Equal(1, import.Data!["successCount"].D());
        var id = import.Data["results"]!.AsArray().Single(x => x!["success"]!.GetValue<bool>())!["id"].S();
        var saved = (await api.Get($"/suppliers/{id}")).Data!;
        Assert.Equal("SA0380000000608010167519", saved["iban"].S()); // محفوظ بلا مسافات
        Assert.False(string.IsNullOrWhiteSpace(saved["code"].S()));    // الكود يُولَّد عند غيابه
    }
}
