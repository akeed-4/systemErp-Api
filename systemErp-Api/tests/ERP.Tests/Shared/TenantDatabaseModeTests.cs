using ERP.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace ERP.Tests;

/// <summary>
/// قاعدة المنشأة المشتركة أو المستقلة: يختارها مدير المنصة وحده عند الإنشاء، والتسجيل العام لا يحملها، والمنشأة لا تغيّرها؛
/// وبيانات المنشأة المستقلة تُكتب فعلاً في قاعدتها لا في المشتركة، ودليلها (الدخول والاشتراك) يبقى متسقاً بين القاعدتين.
/// </summary>
[Collection("api")]
public class TenantDatabaseModeTests : TestBase
{
    public TenantDatabaseModeTests(ErpFactory f) : base(f) { }

    private async Task<Client> PlatformAdminAsync()
    {
        var admin = await NewTenantAsync("شركة مدير المنصة");
        var host = Factory.WithWebHostBuilder(b => b.UseSetting("Platform:AdminEmails:0", admin.Email));
        return await admin.LoginAsAsync(host.CreateClient(), admin.Email, "Passw0rd!");
    }

    private static object NewCompany(string email, string databaseMode = "shared") => new
    {
        companyNameAr = "منشأة " + email[..6], companyNameEn = "Co", vatNumber = Client.NewVat(), crNumber = "1010", city = "الرياض",
        address = "x", phone = "050", email, planId = "professional", billingCycle = "yearly",
        adminName = "المالك", adminEmail = email, adminPhone = "050", password = "Passw0rd!", databaseMode,
    };

    private static string NewEmail() => $"d{Guid.NewGuid():N}@test.com";

    private string DedicatedName(Guid tenantId) => $"{new SqlConnectionStringBuilder(Factory.ConnectionString).InitialCatalog}_t{tenantId:N}";

    private string ConnectionTo(string database) => new SqlConnectionStringBuilder(Factory.ConnectionString) { InitialCatalog = database }.ConnectionString;

    private async Task<int> CountAsync(string database, string sql, Guid tenantId)
    {
        await using var conn = new SqlConnection(ConnectionTo(database));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@t", tenantId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<bool> DatabaseExistsAsync(string database)
    {
        await using var conn = new SqlConnection(ConnectionTo("master"));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @n", conn);
        cmd.Parameters.AddWithValue("@n", database);
        return (int)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private string Catalog => new SqlConnectionStringBuilder(Factory.ConnectionString).InitialCatalog;

    [Fact]
    public async Task Public_signup_ignores_a_database_mode_in_the_payload_and_stays_shared()
    {
        var email = NewEmail();
        var signup = await new Client(NewHttp()).SendAsync(HttpMethod.Post, "/auth/RegisterCompany", NewCompany(email, "dedicated"), anonymous: true);
        Assert.Equal(200, signup.Status);
        var tenantId = Guid.Parse(signup.Body!["tenantId"].S());

        Assert.False(await DatabaseExistsAsync(DedicatedName(tenantId)));
        Assert.Equal(0, await CountAsync(Catalog, "SELECT COUNT(*) FROM TenantDatabase WHERE TenantId = @t", tenantId));
        var admin = await PlatformAdminAsync();
        var row = (await admin.Get("/platform/tenants")).Data!.AsArray().First(t => t!["tenantId"].G() == tenantId)!;
        Assert.Equal("shared", row["databaseMode"].S());
    }

    [Fact]
    public async Task Tenant_users_cannot_create_tenants_or_provision_databases()
    {
        var owner = await NewTenantAsync();
        Assert.Equal(403, (await owner.Post("/platform/tenants", NewCompany(NewEmail(), "dedicated"))).Status);
        Assert.Equal(403, (await owner.Post($"/platform/tenants/{owner.TenantId}/database/provision")).Status);
        // تعديل بيانات المنشأة لا يحمل نوع القاعدة: الحقل المرسَل يُتجاهل ولا ينشأ ربط
        await owner.Put("/company", new { nameAr = "اسم جديد", vatNumber = (string?)null, databaseMode = "dedicated" });
        Assert.Equal(0, await CountAsync(Catalog, "SELECT COUNT(*) FROM TenantDatabase WHERE TenantId = @t", owner.TenantId));
    }

    [Fact]
    public async Task Platform_admin_creates_a_dedicated_tenant_whose_data_lives_only_in_its_database()
    {
        var admin = await PlatformAdminAsync();
        var email = NewEmail();
        var created = await admin.Post("/platform/tenants", NewCompany(email, "dedicated"));
        Assert.Equal(200, created.Status);
        Assert.Equal("dedicated", created.Data!["databaseMode"].S());
        Assert.Equal("ready", created.Data["databaseStatus"].S());
        var tenantId = created.Data["tenantId"].G();
        var dedicated = DedicatedName(tenantId);
        Assert.True(await DatabaseExistsAsync(dedicated));

        // نص الاتصال مشفَّر في الدليل (لا يظهر اسم القاعدة فيه) ولا يُعاد في الاستجابة
        await using (var conn = new SqlConnection(Factory.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT ConnectionStringEncrypted FROM TenantDatabase WHERE TenantId = @t", conn);
            cmd.Parameters.AddWithValue("@t", tenantId);
            var stored = (string)(await cmd.ExecuteScalarAsync())!;
            Assert.DoesNotContain(dedicated, stored);
        }
        Assert.DoesNotContain("connection", created.Body!.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        // المالك يدخل (من الدليل) ويعمل على قاعدته
        var owner = await admin.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(owner.Token);
        var customer = await owner.Post("/customers", new { nameAr = "عميل القاعدة المستقلة", phone = "050", city = "الرياض", status = "active" });
        Assert.Equal(201, customer.Status);

        const string customers = "SELECT COUNT(*) FROM Customer WHERE TenantId = @t";
        Assert.Equal(1, await CountAsync(dedicated, customers, tenantId));
        Assert.Equal(0, await CountAsync(Catalog, customers, tenantId));
        // البيانات الافتتاحية (دليل الحسابات) في قاعدتها لا في المشتركة
        const string accounts = "SELECT COUNT(*) FROM Account WHERE TenantId = @t";
        Assert.True(await CountAsync(dedicated, accounts, tenantId) > 0);
        Assert.Equal(0, await CountAsync(Catalog, accounts, tenantId));
        Assert.Equal(200, (await owner.Get($"/customers/{customer.Data!["id"].S()}")).Status);
    }

    [Fact]
    public async Task Directory_stays_consistent_between_the_dedicated_and_shared_databases()
    {
        var admin = await PlatformAdminAsync();
        var email = NewEmail();
        var tenantId = (await admin.Post("/platform/tenants", NewCompany(email, "dedicated"))).Data!["tenantId"].G();
        var owner = await admin.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        // مستخدم يُضاف في القاعدة المستقلة يدخل (الدخول يقرأ الدليل)
        var staffEmail = NewEmail();
        Assert.Equal(200, (await owner.Post("/users", new { name = "محاسب", email = staffEmail, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var staff = await owner.LoginAsAsync(NewHttp(), staffEmail, "Passw0rd!");
        Assert.NotNull(staff.Token);
        // والبريد نفسه مرفوض في منشأة أخرى (التفرّد عبر المنشآت من الدليل)
        var other = await NewTenantAsync();
        Assert.Equal(409, (await other.Post("/users", new { name = "x", email = staffEmail, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);

        // تعديل المنصة لاشتراكها يصل لقاعدتها (بوابة الاشتراك تقرأ منها) وللدليل (لوحة المنصة)
        Assert.Equal(200, (await admin.Post($"/platform/tenants/{tenantId}/subscriptions/modules", new { modules = new[] { "hr" } })).Status);
        var current = await owner.Get("/subscriptions/current");
        Assert.Contains("hr", current.Data!["modules"]!.AsArray().Select(m => m.S()));
        var row = (await admin.Get("/platform/tenants")).Data!.AsArray().First(t => t!["tenantId"].G() == tenantId)!;
        Assert.Contains("hr", row["subscription"]!["modules"]!.AsArray().Select(m => m.S()));
        Assert.Equal(2, await CountAsync(DedicatedName(tenantId), "SELECT COUNT(*) FROM [User] WHERE TenantId = @t", tenantId));
        Assert.Equal(2, await CountAsync(Catalog, "SELECT COUNT(*) FROM [User] WHERE TenantId = @t", tenantId));
    }

    [Fact]
    public async Task Sales_and_purchases_work_end_to_end_on_a_dedicated_database()
    {
        var admin = await PlatformAdminAsync();
        var email = NewEmail();
        var tenantId = (await admin.Post("/platform/tenants", NewCompany(email, "dedicated"))).Data!["tenantId"].G();
        var api = await admin.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        var dedicated = DedicatedName(tenantId);

        var product = await SeedProductAsync(api, stock: 6, cost: 40);
        var (supplier, _) = await SeedSupplierAsync(api);
        var pur = await api.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier,
            items = new[] { new { itemId = product, quantity = 4, unitPrice = 50, vatRate = 15 } } });
        Assert.Equal(201, pur.Status);
        var sale = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal(201, sale.Status);
        Assert.Equal("posted", sale.Data!["status"].S());
        Assert.Equal(230, sale.Data["grandTotal"].D());

        // المخزون والتكلفة ورصيد المورد والقيود كما في القاعدة المشتركة
        var p = (await api.Get($"/products/{product}")).Data!;
        Assert.Equal(8, p["currentStock"].D());
        Assert.Equal(44, p["averageCost"].D());
        Assert.Equal(230, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
        Assert.True(d > 0);

        // الفواتير والقيود في قاعدتها وحدها
        foreach (var table in new[] { "SalesInvoice", "PurchaseInvoice", "JournalEntry" })
        {
            Assert.True(await CountAsync(dedicated, $"SELECT COUNT(*) FROM {table} WHERE TenantId = @t", tenantId) > 0, table);
            Assert.Equal(0, await CountAsync(Catalog, $"SELECT COUNT(*) FROM {table} WHERE TenantId = @t", tenantId));
        }
    }

    [Fact]
    public async Task Platform_admin_switches_the_default_mode_for_public_signups()
    {
        var owner = await NewTenantAsync();
        Assert.Equal(403, (await owner.Get("/platform/settings")).Status);
        Assert.Equal(403, (await owner.Put("/platform/settings", new { defaultDatabaseMode = "dedicated" })).Status);

        var admin = await PlatformAdminAsync();
        Assert.Equal("shared", (await admin.Get("/platform/settings")).Data!["defaultDatabaseMode"].S());
        Assert.Equal(400, (await admin.Put("/platform/settings", new { defaultDatabaseMode = "bogus" })).Status);
        try
        {
            var saved = await admin.Put("/platform/settings", new { defaultDatabaseMode = "dedicated" });
            Assert.Equal(200, saved.Status);
            Assert.Equal("dedicated", saved.Data!["defaultDatabaseMode"].S());
            Assert.True(saved.Data["isCustomized"]!.GetValue<bool>());
            Assert.Equal(admin.Email, saved.Data["updatedBy"].S());

            // التسجيل العام التالي يتبع الإعداد (والحقل المرسَل فيه يُتجاهل كما هو)
            var email = NewEmail();
            var signup = await new Client(NewHttp()).SendAsync(HttpMethod.Post, "/auth/RegisterCompany", NewCompany(email, "shared"), anonymous: true);
            Assert.Equal(200, signup.Status);
            var tenantId = Guid.Parse(signup.Body!["tenantId"].S());
            Assert.True(await DatabaseExistsAsync(DedicatedName(tenantId)));
            var row = (await admin.Get("/platform/tenants")).Data!.AsArray().First(t => t!["tenantId"].G() == tenantId)!;
            Assert.Equal("dedicated", row["databaseMode"].S());
            Assert.Equal("ready", row["databaseStatus"].S());
            var signedUp = await admin.LoginAsAsync(NewHttp(), email, "Passw0rd!");
            Assert.Equal(201, (await signedUp.Post("/customers", new { nameAr = "عميل", phone = "050", city = "الرياض", status = "active" })).Status);
            Assert.Equal(1, await CountAsync(DedicatedName(tenantId), "SELECT COUNT(*) FROM Customer WHERE TenantId = @t", tenantId));

            // المنشآت القائمة لا تتأثر بتغيير الإعداد
            Assert.Equal(0, await CountAsync(Catalog, "SELECT COUNT(*) FROM TenantDatabase WHERE TenantId = @t", owner.TenantId));
        }
        finally
        {
            await admin.Put("/platform/settings", new { defaultDatabaseMode = "shared" });
        }
    }

    [Fact]
    public async Task Platform_admin_creates_a_shared_tenant_on_the_shared_database()
    {
        var admin = await PlatformAdminAsync();
        var email = NewEmail();
        var created = await admin.Post("/platform/tenants", NewCompany(email));
        Assert.Equal(200, created.Status);
        Assert.Equal("shared", created.Data!["databaseMode"].S());
        var tenantId = created.Data["tenantId"].G();
        Assert.False(await DatabaseExistsAsync(DedicatedName(tenantId)));

        var owner = await admin.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.Equal(201, (await owner.Post("/customers", new { nameAr = "عميل مشترك", phone = "050", city = "الرياض", status = "active" })).Status);
        Assert.Equal(1, await CountAsync(Catalog, "SELECT COUNT(*) FROM Customer WHERE TenantId = @t", tenantId));
        // نوع قاعدة المنشأة المشتركة لا يتغيّر بعد إنشائها
        Assert.Equal(400, (await admin.Post($"/platform/tenants/{tenantId}/database/provision")).Status);
    }
}
