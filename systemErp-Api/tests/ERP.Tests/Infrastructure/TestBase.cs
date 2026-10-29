using ERP.Core.Models.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests.Infrastructure;

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ErpFactory> { }

/// <summary>قاعدة الاختبارات: يسجّل كل اختبار منشأة جديدة معزولة عن غيره.</summary>
public abstract class TestBase
{
    protected readonly ErpFactory Factory;
    protected TestBase(ErpFactory factory) => Factory = factory;

    protected HttpClient NewHttp() => Factory.CreateClient();

    /// <summary>ملف PDF صغير صالح (توقيع %PDF) للمرفقات.</summary>
    protected static readonly byte[] SmallPdf = "%PDF-1.4 medical report"u8.ToArray();

    /// <summary>رفع ملف (multipart) إلى مسار API باسم الحقل file، ويعيد رمز الحالة.</summary>
    protected async Task<int> UploadFileAsync(Client api, string path, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1" + path) { Content = form };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", api.Token);
        return (int)(await NewHttp().SendAsync(request)).StatusCode;
    }
    protected async Task<Client> NewTenantAsync(string name = "شركة اختبار") => await new Client(NewHttp()).RegisterAsync(name);

    protected async Task<Client> NewTenantWithModulesAsync(params string[] modules)
    {
        var admin = await NewTenantAsync("شركة مدير المنصة للاختبار");
        var host = Factory.WithWebHostBuilder(builder => builder.UseSetting("Platform:AdminEmails:0", admin.Email));
        var root = await admin.LoginAsAsync(host.CreateClient(), admin.Email, "Passw0rd!");
        var tenant = await NewTenantAsync("شركة اختبار " + string.Join("+", modules));
        Assert.Equal(200, (await root.Post($"/platform/tenants/{tenant.TenantId}/subscriptions/modules", new { modules })).Status);
        return tenant;
    }

    /// <summary>صنف بسعر 100 وضريبة 15% ومخزون افتتاحي بتكلفة معلومة.</summary>
    protected static async Task<Guid> SeedProductAsync(Client api, string sku = "P1", decimal stock = 10, decimal cost = 40, decimal price = 100)
    {
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        var p = await api.Post("/products", new { sku, nameAr = "منتج " + sku, category = "C1", unit = "PCS", sellingPrice = price, vatRate = 15 });
        Assert.Equal(201, p.Status);
        var id = p.Data!["id"].G();
        if (stock > 0)
        {
            var m = await api.Post("/stockmovements/adjust", new { itemId = id, type = "adjustment_in", quantity = stock, unitCost = cost, referenceNumber = "OPEN", counterAccountCode = "33" }); // مخزون أول المدة مقابل الأرصدة الافتتاحية
            Assert.Equal(200, m.Status);
        }
        return id;
    }

    protected static async Task<(Guid Id, string AccountCode)> SeedCustomerAsync(Client api, decimal creditLimit = 100000, string name = "عميل آجل")
    {
        var c = await api.Post("/customers", new { nameAr = name, phone = "050", city = "الرياض", creditLimit, creditPeriodDays = 30, status = "active" });
        Assert.Equal(201, c.Status);
        return (c.Data!["id"].G(), c.Data["accountCode"].S());
    }

    protected static async Task<(Guid Id, string AccountCode)> SeedSupplierAsync(Client api, string name = "مورد")
    {
        var s = await api.Post("/suppliers", new { nameAr = name, phone = "050", city = "جدة", status = "active", vatNumber = Client.NewVat() });
        Assert.Equal(201, s.Status);
        return (s.Data!["id"].G(), s.Data["accountCode"].S());
    }

    /// <summary>يعلّم فواتير بيع المنشأة ومرتجعاتها مُبلَّغة للهيئة: لا محاكي لهيئة الزكاة في الاختبارات.</summary>
    /// <summary>نقطة بيع معرّفة (المرتجعات والخصم مسموحة) — شرط فتح الوردية.</summary>
    protected static async Task<Guid> SeedPosTerminalAsync(Client admin, string name = "POS-1")
    {
        var code = $"T{Guid.NewGuid():N}"[..10];
        var t = await admin.Post("/pos/terminals", new { code, name, deviceIdentifier = code, allowReturns = true, allowDiscount = true });
        Assert.True(t.Status is 200 or 201, t.Body?.ToJsonString());
        return t.Data!["id"].G();
    }

    /// <summary>يعرّف نقطة بيع (بحساب المدير) ويفتح عليها وردية لـ <paramref name="cashier"/> (افتراضياً المدير نفسه).</summary>
    protected static async Task<Guid> OpenPosShiftAsync(Client admin, decimal openingCash = 0, string name = "POS-1", Client? cashier = null)
    {
        var terminal = await SeedPosTerminalAsync(admin, name);
        var open = await (cashier ?? admin).Post("/pos/shifts/open", new { openingCash, posTerminalId = terminal });
        Assert.True(open.Status == 200, open.Body?.ToJsonString());
        return terminal;
    }

    protected async Task MarkSalesReportedToZatcaAsync(Client api)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var docs = await db.Set<ERP.Core.Models.Accounting.InvoiceBase>().IgnoreQueryFilters()
            .Where(i => i.TenantId == api.TenantId && (i.Kind == InvoiceKind.Sales || i.Kind == InvoiceKind.SalesReturn)).ToListAsync();
        foreach (var d in docs) d.ZatcaStatus = ZatcaSubmissionStatus.Reported;
        await db.SaveChangesAsync();
    }

    protected static (decimal Debit, decimal Credit) Totals(Res trialBalance)
    {
        decimal d = 0, c = 0;
        foreach (var row in trialBalance.Data!.AsArray()) { d += row!["periodDebit"].D(); c += row["periodCredit"].D(); }
        return (d, c);
    }
}
