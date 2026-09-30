using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using ERP.Core.Resources;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>رسائل الـ API بالعربية افتراضياً وبالإنجليزية عند Accept-Language: en، دون تغيير تنسيق الأرقام.</summary>
[Collection("api")]
public class LocalizationTests : TestBase
{
    public LocalizationTests(ErpFactory f) : base(f) { }

    [Theory]
    [InlineData(null, "الصنف غير موجود")]
    [InlineData("ar", "الصنف غير موجود")]
    [InlineData("en", "Item not found")]
    [InlineData("en-US,en;q=0.9", "Item not found")]
    [InlineData("fr-FR", "الصنف غير موجود")] // لغة غير مدعومة: العربية الافتراضية
    public async Task Error_messages_follow_the_accept_language_header(string? language, string expected)
    {
        var api = await NewTenantAsync();
        api.Language = language;
        var r = await api.Get($"/products/{Guid.NewGuid()}");
        Assert.Equal(404, r.Status);
        Assert.Equal(expected, r.Body!["message"].S());
    }

    [Fact]
    public async Task Validation_success_and_formatted_messages_are_translated()
    {
        var api = await NewTenantAsync();
        api.Language = "en";

        var invalid = await api.Post("/inventorycounts", new { scope = "items", countDate = DateTime.UtcNow, lines = Array.Empty<object>() });
        Assert.Equal(400, invalid.Status);
        Assert.Contains("At least one count line is required.", invalid.Body!["errors"]!.AsArray().Select(e => e.S()));

        var unbalanced = await api.Post("/journalentries", new { description = "x", lines = new[] { new { accountCode = "1111", debit = 10, credit = 0 }, new { accountCode = "311", debit = 0, credit = 5 } } });
        Assert.Equal("The journal entry is unbalanced: debit 10.00 ≠ credit 5.00.", unbalanced.Body!["message"].S());

        var created = await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        Assert.Equal("Created successfully", created.Body!["message"].S());

        api.Language = "ar";
        Assert.Equal("تم الإنشاء بنجاح", (await api.Post("/productcategories", new { code = "C2", nameAr = "تصنيف 2" })).Body!["message"].S());
    }

    [Fact]
    public async Task Unauthenticated_requests_are_answered_in_the_requested_language()
    {
        var anonymous = new Client(NewHttp()) { Language = "en" };
        var r = await anonymous.Get("/products");
        Assert.Equal(401, r.Status);
        Assert.Equal("You must sign in.", r.Body!["message"].S());
    }

    [Fact]
    public void Every_arabic_message_has_an_english_translation_with_the_same_placeholders()
    {
        static Dictionary<string, string> Load(CultureInfo culture) =>
            Messages.ResourceManager.GetResourceSet(culture, true, false)!.Cast<DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

        var ar = Load(CultureInfo.InvariantCulture); // الموارد المحايدة = العربية
        var en = Load(new CultureInfo("en"));
        Assert.NotEmpty(ar);
        Assert.Empty(ar.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(ar.Keys));

        static string Placeholders(string s) => string.Join("|", Regex.Matches(s, @"\{\d+(:[^}]*)?\}").Select(m => m.Value).OrderBy(x => x));
        var mismatched = ar.Where(kv => string.IsNullOrWhiteSpace(en[kv.Key]) || Placeholders(kv.Value) != Placeholders(en[kv.Key])).Select(kv => kv.Key).ToList();
        Assert.Empty(mismatched);
    }
}
