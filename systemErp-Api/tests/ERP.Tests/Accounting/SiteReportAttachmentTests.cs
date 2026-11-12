using System.Net.Http.Headers;
using ERP.Core.Models.Accounting;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// أرشيف مستندات بنود التقرير اليومي (كأرشيف بنود المستخلص): رفع لكل بند بتصنيف وملاحظة، وحد 2 ميغابايت للملف، ونوع الملف من توقيعه،
/// والحذف في المسودة لمن يُعدّ التقارير وبعد التقديم بصلاحية الحذف، والأرشيف يُحذف مع مسودة التقرير.
/// </summary>
[Collection("api")]
public class SiteReportAttachmentTests : TestBase
{
    public SiteReportAttachmentTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Day = new(2026, 11, 1);
    private const int MaxBytes = 2 * 1024 * 1024;

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private static byte[] Pdf(int size)
    {
        var bytes = new byte[size];
        "%PDF-1.4 "u8.CopyTo(bytes);
        return bytes;
    }

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

    private sealed record Project(string Id, Guid Concrete, Guid Steel, Guid Group);

    /// <summary>عقد مقاولات مبسط ببندين ومجموعة لها فرع، وساعات عمل 07:00–11:00.</summary>
    private static async Task<Project> ProjectAsync(Client api)
    {
        var created = await api.Post("/commercialcontracts", new
        {
            title = "أعمال خرسانية", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل",
            startDate = Day.AddDays(-10), endDate = Day.AddMonths(6), vatRate = 15,
            items = new object[]
            {
                new { description = "صب خرسانة", unit = "م3", quantity = 500, unitPrice = 300, vatRate = 15, lineKind = "measured", sortOrder = 1 },
                new { description = "حديد تسليح", unit = "طن", quantity = 50, unitPrice = 3000, vatRate = 15, lineKind = "measured", sortOrder = 2 },
                new { description = "أعمال تشطيب", unit = "مقطوعية", quantity = 1, unitPrice = 0, vatRate = 15, lineKind = "measured", sortOrder = 3 },
                new { description = "دهان", unit = "م2", quantity = 100, unitPrice = 20, vatRate = 15, lineKind = "measured", isSubItem = true, sortOrder = 4 },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "create");
        var id = created.Data!["id"].S();
        var items = created.Data["items"]!.AsArray();
        Guid Item(string d) => items.First(i => i!["description"].S() == d)!["id"].G();
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "11:00" }), "hours");
        return new Project(id, Item("صب خرسانة"), Item("حديد تسليح"), Item("أعمال تشطيب"));
    }

    private static async Task<string> ReportAsync(Client api, Project p, DateTime day)
    {
        var r = await api.Post($"/commercialcontracts/{p.Id}/site-reports", new
        {
            reportDate = day,
            hours = new[] { new { start = "07:00", end = "08:00", contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = 9 } },
        });
        Ok(r, "report");
        return r.Data!["id"].S();
    }

    private static string Base(Project p, string reportId) => $"/commercialcontracts/{p.Id}/site-reports/{reportId}";

    /// <summary>رفع مستند لبند بتصنيف وملاحظة (multipart)؛ يعيد الحالة والجسم.</summary>
    private async Task<(int Status, string Body)> UploadAsync(Client api, string path, string fileName, byte[] content, string? category = null, string? note = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (category != null) form.Add(new StringContent(category), "category");
        if (note != null) form.Add(new StringContent(note), "note");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1" + path) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        var response = await NewHttp().SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<int> ArchivedCountAsync(string reportId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return await db.Set<SiteReportItemAttachment>().IgnoreQueryFilters().CountAsync(a => a.ReportId == Guid.Parse(reportId));
    }

    [Fact]
    public async Task Each_report_item_archives_documents_with_category_note_and_download()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        var rid = await ReportAsync(api, p, Day);
        var b = Base(p, rid);

        var summary = (await api.Get($"{b}/attachments/summary")).Data!;
        Assert.Equal(MaxBytes, summary["maxBytes"]!.GetValue<long>());
        Assert.Equal(20, summary["maxPerItem"]!.GetValue<int>());
        Assert.True(summary["canAdd"]!.GetValue<bool>());
        Assert.Empty(summary["counts"]!.AsObject());

        var pdf = Pdf(1024);
        var up = await UploadAsync(api, $"{b}/items/{p.Concrete}/attachments", "محضر.pdf", pdf, "inspection_report", "فحص الهبوط");
        Assert.True(up.Status is 200 or 201, up.Body);
        Assert.Equal(200, (await UploadAsync(api, $"{b}/items/{p.Concrete}/attachments", "site.png", Png, "photo")).Status);
        Assert.Equal(200, (await UploadAsync(api, $"{b}/items/{p.Steel}/attachments", "invoice.pdf", Pdf(200))).Status); // بلا تصنيف = أخرى

        var concrete = (await api.Get($"{b}/attachments?itemId={p.Concrete}")).Data!.AsArray();
        Assert.Equal(2, concrete.Count);
        var report = concrete.First(a => a!["fileName"].S() == "محضر.pdf")!;
        Assert.Equal("inspection_report", report["category"].S());
        Assert.Equal("فحص الهبوط", report["note"].S());
        Assert.Equal("application/pdf", report["contentType"].S());
        Assert.Equal(1024, report["size"]!.GetValue<long>());
        Assert.True(report["canDelete"]!.GetValue<bool>());
        Assert.Equal("other", (await api.Get($"{b}/attachments?itemId={p.Steel}")).Data!.AsArray().Single()!["category"].S());

        var counts = (await api.Get($"{b}/attachments/summary")).Data!["counts"]!.AsObject();
        Assert.Equal(2, counts[p.Concrete.ToString()]!.GetValue<int>());
        Assert.Equal(1, counts[p.Steel.ToString()]!.GetValue<int>());

        // التنزيل يعيد المحتوى نفسه
        var download = new HttpRequestMessage(HttpMethod.Get, $"/api/v1{b}/attachments/{report["id"].S()}");
        download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        var file = await NewHttp().SendAsync(download);
        Assert.Equal(200, (int)file.StatusCode);
        Assert.Equal(pdf, await file.Content.ReadAsByteArrayAsync());

        // الأرشيف لا يغيّر التقرير
        Assert.Equal("draft", (await api.Get(b)).Data!["status"].S());
    }

    [Fact]
    public async Task Upload_is_limited_to_2_megabytes_and_valid_items_types_and_categories()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        var rid = await ReportAsync(api, p, Day);
        var url = $"{Base(p, rid)}/items/{p.Concrete}/attachments";

        Assert.Equal(200, (await UploadAsync(api, url, "max.pdf", Pdf(MaxBytes))).Status); // الحد نفسه مقبول
        var tooLarge = await UploadAsync(api, url, "big.pdf", Pdf(MaxBytes + 1));
        Assert.Equal(400, tooLarge.Status);
        Assert.Contains("2", tooLarge.Body);

        Assert.Equal(400, (await UploadAsync(api, url, "notes.txt", "plain text"u8.ToArray())).Status);          // نوع غير مسموح
        Assert.Equal(400, (await UploadAsync(api, url, "fake.pdf", "not a pdf at all"u8.ToArray())).Status);     // الامتداد لا يكفي
        Assert.Equal(400, (await UploadAsync(api, url, "a.pdf", Pdf(100), "contract")).Status);                   // تصنيف غير صالح
        Assert.Equal(400, (await UploadAsync(api, url, "empty.pdf", Array.Empty<byte>())).Status);
        Assert.Equal(400, (await UploadAsync(api, $"{Base(p, rid)}/items/{Guid.NewGuid()}/attachments", "a.pdf", Pdf(100))).Status); // بند غريب
        Assert.Equal(400, (await UploadAsync(api, $"{Base(p, rid)}/items/{p.Group}/attachments", "a.pdf", Pdf(100))).Status);       // بند مجموعة
        Assert.Equal(404, (await UploadAsync(api, $"{Base(p, Guid.NewGuid().ToString())}/items/{p.Concrete}/attachments", "a.pdf", Pdf(100))).Status);

        // تقرير مشروع آخر لا يُقرأ أرشيفه عبر هذا المشروع
        var other = await ProjectAsync(api);
        Assert.Equal(404, (await api.Get($"/commercialcontracts/{other.Id}/site-reports/{rid}/attachments")).Status);

        Assert.Equal(1, await ArchivedCountAsync(rid));
    }

    [Fact]
    public async Task Preparer_archives_in_any_state_but_deletes_only_in_draft_and_delete_permission_is_needed_after_submit()
    {
        var owner = await NewConstructionTenantAsync();
        Ok(await owner.Post("/construction-projects/role-templates"), "templates");
        var roleId = (await owner.Get("/roles")).Data!.AsArray().First(r => r!["nameAr"].S() == "مدير المشروع")!["id"].G();
        var email = $"s{Guid.NewGuid():N}@test.com";
        var user = await owner.Post("/users", new { name = "مشرف", email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId });
        Ok(user, "supervisor");
        var supervisor = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        var p = await ProjectAsync(owner);
        Ok(await owner.Post($"/commercialcontracts/{p.Id}/project/members", new { userId = user.Data!["id"].G(), role = "site_supervisor" }), "member");

        var rid = await ReportAsync(supervisor, p, Day);
        var b = Base(p, rid);
        var url = $"{b}/items/{p.Concrete}/attachments";
        Assert.Equal(200, (await UploadAsync(supervisor, url, "a.pdf", Pdf(100))).Status);
        var draftDoc = (await supervisor.Get($"{b}/attachments")).Data!.AsArray().Single()!;
        Assert.True(draftDoc["canDelete"]!.GetValue<bool>());
        Ok(await supervisor.Delete($"{b}/attachments/{draftDoc["id"].S()}"), "delete in draft");

        Assert.Equal(200, (await UploadAsync(supervisor, url, "b.pdf", Pdf(100))).Status);
        Ok(await supervisor.Post($"{b}/submit"), "submit");

        // بعد التقديم: الإضافة مستمرة، والحذف يتطلب صلاحية الحذف على التقارير اليومية
        Assert.Equal(200, (await UploadAsync(supervisor, url, "c.pdf", Pdf(100))).Status);
        var summary = (await supervisor.Get($"{b}/attachments/summary")).Data!;
        Assert.True(summary["canAdd"]!.GetValue<bool>());
        Assert.False(summary["canDelete"]!.GetValue<bool>());
        var docs = (await supervisor.Get($"{b}/attachments")).Data!.AsArray();
        Assert.Equal(2, docs.Count);
        Assert.Equal(403, (await supervisor.Delete($"{b}/attachments/{docs[0]!["id"].S()}")).Status);
        Ok(await owner.Delete($"{b}/attachments/{docs[0]!["id"].S()}"), "owner deletes after submit");
        Assert.Equal(1, await ArchivedCountAsync(rid));
    }

    [Fact]
    public async Task Deleting_a_draft_report_removes_its_archive()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        var rid = await ReportAsync(api, p, Day);
        Assert.Equal(200, (await UploadAsync(api, $"{Base(p, rid)}/items/{p.Concrete}/attachments", "a.pdf", Pdf(100))).Status);
        Assert.Equal(200, (await UploadAsync(api, $"{Base(p, rid)}/items/{p.Steel}/attachments", "b.png", Png)).Status);
        Assert.Equal(2, await ArchivedCountAsync(rid));

        Ok(await api.Delete(Base(p, rid)), "delete report");
        Assert.Equal(0, await ArchivedCountAsync(rid));
        Assert.Equal(404, (await api.Get($"{Base(p, rid)}/attachments")).Status);
    }
}
