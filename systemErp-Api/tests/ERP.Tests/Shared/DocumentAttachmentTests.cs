using System.Net.Http.Headers;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// المرفقات المشتركة (أساس الورش والتأجير) بمالك اختبار (<see cref="TestAttachmentOwner"/>: عميل بوحدة الورش): الرفع بتصنيف وملاحظة
/// ووقت التقاط، والمحتوى منفصل عن السجل عند مزوده، والتنزيل بالمحتوى نفسه، والحذف يحذف المحتوى، والحدود (النوع والحجم والعدد
/// والتصنيف)، وترخيص الوحدة، وحالة المالك، وعزل المنشآت.
/// </summary>
[Collection("api")]
public class DocumentAttachmentTests : TestBase
{
    public DocumentAttachmentTests(ErpFactory f) : base(f) { }

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
    private static string Path(Guid owner) => $"/attachments/{TestAttachmentOwner.Type}/{owner}";

    private Task<Client> WorkshopTenantAsync() => NewTenantWithModulesAsync("accounting", "workshop");

    private async Task<(int Status, string Body)> UploadAsync(Client api, Guid owner, string fileName, byte[] content, string? category = null,
        string? note = null, DateTime? capturedAt = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (category != null) form.Add(new StringContent(category), "category");
        if (note != null) form.Add(new StringContent(note), "note");
        if (capturedAt != null) form.Add(new StringContent(capturedAt.Value.ToString("O")), "capturedAt");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1" + Path(owner)) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        var response = await NewHttp().SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<(int Status, byte[] Content)> DownloadAsync(Client api, string attachmentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/attachments/file/{attachmentId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        var response = await NewHttp().SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsByteArrayAsync());
    }

    private async Task<T> DbAsync<T>(Func<ERP.Service.Data.ErpDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>());
    }

    [Fact]
    public async Task Upload_list_download_and_delete_keep_content_separate_from_the_record()
    {
        var api = await WorkshopTenantAsync();
        var (owner, _) = await SeedCustomerAsync(api);

        var summary = (await api.Get($"{Path(owner)}/summary")).Data!;
        Assert.Equal(0, summary["count"]!.GetValue<int>());
        Assert.True(summary["canAdd"]!.GetValue<bool>());
        Assert.Equal(TestAttachmentOwner.Max, summary["maxCount"]!.GetValue<int>());
        Assert.Equal(64 * 1024, summary["maxBytes"]!.GetValue<long>());
        Assert.Equal(new[] { "photo", "damage", "other" }, summary["categories"]!.AsArray().Select(c => c.S()));

        var captured = DateTime.UtcNow.AddMinutes(-30);
        var up = await UploadAsync(api, owner, "front.png", Png, "damage", "خدش الباب الأمامي", captured);
        Assert.Equal(200, up.Status);
        Assert.Equal(200, (await UploadAsync(api, owner, "report.pdf", SmallPdf)).Status); // بلا تصنيف = أخرى
        // وقت التقاط مستقبلي (ساعة جهاز خاطئة) يُهمل
        Assert.Equal(200, (await UploadAsync(api, owner, "later.png", Png, "photo", capturedAt: DateTime.UtcNow.AddDays(2))).Status);

        var list = (await api.Get(Path(owner))).Data!.AsArray();
        Assert.Equal(3, list.Count);
        var damage = list.First(a => a!["fileName"].S() == "front.png")!;
        Assert.Equal("damage", damage["category"].S());
        Assert.Equal("خدش الباب الأمامي", damage["note"].S());
        Assert.Equal("image/png", damage["contentType"].S());
        Assert.Equal(Png.Length, damage["size"]!.GetValue<long>());
        Assert.NotNull(damage["capturedAt"]);
        Assert.True(damage["canDelete"]!.GetValue<bool>());
        Assert.Equal("other", list.First(a => a!["fileName"].S() == "report.pdf")!["category"].S());
        Assert.Null(list.First(a => a!["fileName"].S() == "later.png")!["capturedAt"]);

        // السجل يحمل مزوده ومفتاحه وبصمته، والمحتوى في جدوله
        var id = damage["id"].G();
        var row = await DbAsync(db => db.Set<DocumentAttachment>().IgnoreQueryFilters().SingleAsync(a => a.Id == id));
        Assert.Equal(AttachmentStorageProviders.Database, row.StorageProvider);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Png)), row.Checksum);
        Assert.True(await DbAsync(db => db.Set<DocumentAttachmentContent>().IgnoreQueryFilters().AnyAsync(c => c.AttachmentId == id && c.Id.ToString() == row.StorageKey)));

        var file = await DownloadAsync(api, id.ToString());
        Assert.Equal(200, file.Status);
        Assert.Equal(Png, file.Content);

        Assert.Equal(200, (await api.Delete($"/attachments/{id}")).Status);
        Assert.Equal(2, (await api.Get(Path(owner))).Data!.AsArray().Count);
        Assert.False(await DbAsync(db => db.Set<DocumentAttachmentContent>().IgnoreQueryFilters().AnyAsync(c => c.AttachmentId == id)));
        Assert.Equal(404, (await DownloadAsync(api, id.ToString())).Status);
    }

    [Fact]
    public async Task Type_size_category_and_count_limits_are_enforced()
    {
        var api = await WorkshopTenantAsync();
        var (owner, _) = await SeedCustomerAsync(api);

        Assert.Equal(400, (await UploadAsync(api, owner, "notes.txt", "hello"u8.ToArray())).Status);      // النوع من توقيع الملف
        Assert.Equal(400, (await UploadAsync(api, owner, "fake.png", SmallPdf)).Status);                  // امتداد لا يطابق المحتوى
        var big = new byte[64 * 1024 + 1];
        Png.CopyTo(big, 0);
        Assert.Equal(400, (await UploadAsync(api, owner, "big.png", big)).Status);                        // حد المالك
        Assert.Equal(400, (await UploadAsync(api, owner, "sig.png", Png, "signature")).Status);           // تصنيف خارج تصنيفات المالك

        for (var i = 0; i < TestAttachmentOwner.Max; i++) Assert.Equal(200, (await UploadAsync(api, owner, $"p{i}.png", Png, "photo")).Status);
        Assert.Equal(400, (await UploadAsync(api, owner, "extra.png", Png, "photo")).Status);
    }

    [Fact]
    public async Task Unknown_owner_missing_module_locked_owner_and_other_tenant_are_rejected()
    {
        var api = await WorkshopTenantAsync();
        var (owner, _) = await SeedCustomerAsync(api);
        Assert.Equal(200, (await UploadAsync(api, owner, "a.png", Png)).Status);
        var id = (await api.Get(Path(owner))).Data!.AsArray().Single()!["id"].S();

        Assert.Equal(404, (await api.Get($"/attachments/unknown_type/{owner}")).Status);
        Assert.Equal(404, (await api.Get(Path(Guid.NewGuid()))).Status);

        // منشأة بلا إضافة الورش: الوحدة غير مرخّصة
        var plain = await NewTenantAsync();
        var (plainOwner, _) = await SeedCustomerAsync(plain);
        Assert.Equal(403, (await plain.Get(Path(plainOwner))).Status);
        Assert.Equal(404, (await api.Get(Path(plainOwner))).Status); // عميل منشأة أخرى غير موجود عنده

        // منشأة أخرى بالإضافة نفسها لا ترى مرفق غيرها ولا مالكه
        var other = await WorkshopTenantAsync();
        Assert.Equal(404, (await DownloadAsync(other, id)).Status);
        Assert.Equal(404, (await other.Delete($"/attachments/{id}")).Status);
        Assert.Equal(404, (await other.Get(Path(owner))).Status);

        // المالك في حالة لا تقبل التعديل: لا إضافة ولا حذف، والقراءة باقية
        await DbAsync(async db =>
        {
            var c = await db.Set<Customer>().IgnoreQueryFilters().SingleAsync(x => x.Id == owner);
            c.Status = "inactive";
            return await db.SaveChangesAsync();
        });
        var locked = (await api.Get($"{Path(owner)}/summary")).Data!;
        Assert.False(locked["canAdd"]!.GetValue<bool>());
        Assert.False(locked["canDelete"]!.GetValue<bool>());
        Assert.Equal(403, (await UploadAsync(api, owner, "b.png", Png)).Status);
        Assert.Equal(403, (await api.Delete($"/attachments/{id}")).Status);
        Assert.Equal(200, (await DownloadAsync(api, id)).Status);
    }
}
