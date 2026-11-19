using System.Diagnostics;
using ERP.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace ERP.Tests.Shared;

/// <summary>
/// مُعدّ المستند لا يقرّر فيه (ولا المالك) ويبقى له السحب، والمالك يقرّر في مستندات غيره؛ الحالة تتحدّث فوراً بعد القرار؛
/// وقوائم الفواتير لا تعرض مسودة آخر طلب اعتمادها معلّق أو مرفوض، وتبقى سريعة مع بيانات كبيرة.
/// </summary>
[Collection("api")]
public class ApprovalSelfDecisionAndListTests : TestBase
{
    private readonly ITestOutputHelper _out;
    public ApprovalSelfDecisionAndListTests(ErpFactory f, ITestOutputHelper output) : base(f) => _out = output;

    private async Task<Client> UserAsync(Client owner, string role)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = $"مستخدم {role}", email, phone = "1", password = "Passw0rd!", role })).Status);
        var user = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(user.Token);
        return user;
    }

    private static async Task PolicyAsync(Client owner, string role)
        => Assert.Equal(201, (await owner.Post("/ApprovalPolicies", new
        {
            nameAr = "اعتماد المبيعات", documentTypes = new[] { "sales" }, actionType = "create", minAmountTrigger = 1, isActive = true,
            steps = new[] { new { level = 1, approverRole = role, titleAr = role } },
        })).Status);

    private static async Task<string> RequestAsync(Client requester)
    {
        var check = await requester.Post("/approvals/check", new
        {
            documentType = "sales", actionType = "create", documentId = Guid.NewGuid(), documentNumber = $"S-{Guid.NewGuid():N}"[..10], documentAmount = 5000,
        });
        Assert.True(check.Data!["approvalRequired"]!.GetValue<bool>());
        return check.Data["request"]!["id"].S();
    }

    /// <summary>مسودة فاتورة مبيعات وطلب اعتماد «إنشاء» عليها؛ يعيد (معرّف الفاتورة، معرّف الطلب).</summary>
    private static async Task<(string Invoice, string Request)> DraftWithRequestAsync(Client owner, Guid product)
    {
        var draft = await owner.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } });
        var id = draft.Data!["id"].S();
        var check = await owner.Post("/approvals/check", new { documentType = "sales", actionType = "create", documentId = id,
            documentNumber = draft.Data["invoiceNumber"].S(), documentAmount = 115 });
        return (id, check.Data!["request"]!["id"].S());
    }

    private static async Task<string> StatusAsync(Client api, string requestId)
        => (await api.Get("/approvals?pageSize=200")).Data!["items"]!.AsArray().Single(r => r!["id"].S() == requestId)!["status"].S();

    private static async Task<List<string>> ListedInvoiceIdsAsync(Client api)
        => (await api.Get("/sales-invoices?pageSize=200")).Data!["items"]!.AsArray().Select(i => i!["id"].S()).ToList();

    private static async Task<List<string>> LoadedInvoiceIdsAsync(Client api)
    {
        var res = await api.Get("/sales-invoices/load?take=200&requireTotalCount=true");
        Assert.Equal(200, res.Status);
        return res.Body!["data"]!.AsArray().Select(i => i!["id"].S()).ToList();
    }

    [Fact]
    public async Task Owner_who_prepared_the_document_cannot_reject_or_return_it_but_can_withdraw()
    {
        var owner = await NewTenantAsync();
        await PolicyAsync(owner, "owner");
        var id = await RequestAsync(owner);

        Assert.Equal(403, (await owner.Post($"/approvals/{id}/approve", new { comment = "ذاتي" })).Status);
        Assert.Equal(403, (await owner.Post($"/approvals/{id}/reject", new { comment = "ذاتي" })).Status);
        Assert.Equal(403, (await owner.Post($"/approvals/{id}/return", new { comment = "ذاتي" })).Status);
        Assert.Equal("pending", await StatusAsync(owner, id));

        var withdrawn = await owner.Delete($"/approvals/{id}");
        Assert.Equal(200, withdrawn.Status);
        Assert.Equal("cancelled", withdrawn.Data!["status"].S());
    }

    [Fact]
    public async Task Assigned_approver_cannot_reject_own_document_and_owner_still_decides_on_others_immediately()
    {
        var owner = await NewTenantAsync();
        await PolicyAsync(owner, "general_manager");
        var manager = await UserAsync(owner, "general_manager");
        var otherManager = await UserAsync(owner, "general_manager");

        // معتمِد المستوى بدوره لكنه مُعدّ المستند: لا رفض ولا إعادة ولا اعتماد
        var own = await RequestAsync(manager);
        Assert.Equal(403, (await manager.Post($"/approvals/{own}/reject", new { comment = "ذاتي" })).Status);
        Assert.Equal(403, (await manager.Post($"/approvals/{own}/return", new { comment = "ذاتي" })).Status);
        Assert.Equal(403, (await manager.Post($"/approvals/{own}/approve", new { comment = "ذاتي" })).Status);

        // المالك يرفض مستند غيره، والحالة في الاستجابة والقائمة فوراً
        var rejected = await owner.Post($"/approvals/{own}/reject", new { comment = "السعر غير صحيح" });
        Assert.Equal(200, rejected.Status);
        Assert.Equal("rejected", rejected.Data!["status"].S());
        Assert.Equal("rejected", await StatusAsync(owner, own));
        Assert.Equal("rejected", await StatusAsync(manager, own)); // مُعدّه يرى النتيجة مباشرة

        // ومعتمِد آخر بالدور نفسه يعتمد مستند زميله
        var second = await RequestAsync(manager);
        var approved = await otherManager.Post($"/approvals/{second}/approve", new { comment = "موافق" });
        Assert.Equal(200, approved.Status);
        Assert.Equal("approved", approved.Data!["status"].S());
        Assert.Equal("approved", await StatusAsync(manager, second));
    }

    [Fact]
    public async Task Invoice_lists_hide_pending_and_rejected_drafts_and_show_returned_and_approved_right_after_decision()
    {
        var owner = await NewTenantAsync();
        var product = await SeedProductAsync(owner, stock: 100);
        await PolicyAsync(owner, "general_manager");
        var manager = await UserAsync(owner, "general_manager");

        var pending = await DraftWithRequestAsync(owner, product);
        var rejected = await DraftWithRequestAsync(owner, product);
        var returned = await DraftWithRequestAsync(owner, product);
        var approved = await DraftWithRequestAsync(owner, product);
        var plain = (await owner.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } })).Data!["id"].S();

        Assert.Equal(200, (await manager.Post($"/approvals/{rejected.Request}/reject", new { comment = "مرفوض" })).Status);
        Assert.Equal(200, (await manager.Post($"/approvals/{returned.Request}/return", new { comment = "عدّل" })).Status);
        Assert.Equal(200, (await manager.Post($"/approvals/{approved.Request}/approve", new { comment = "موافق" })).Status);

        // حالة المستند بعد القرار مباشرة
        Assert.Equal("posted", (await owner.Get(await owner.InvoicePath(approved.Invoice))).Data!["status"].S());
        Assert.Equal("draft", (await owner.Get(await owner.InvoicePath(rejected.Invoice))).Data!["status"].S());

        foreach (var ids in new[] { await ListedInvoiceIdsAsync(owner), await LoadedInvoiceIdsAsync(owner) })
        {
            Assert.DoesNotContain(pending.Invoice, ids);
            Assert.DoesNotContain(rejected.Invoice, ids);
            Assert.Contains(returned.Invoice, ids);
            Assert.Contains(approved.Invoice, ids);
            Assert.Contains(plain, ids);
        }

        // إعادة إرسال المرفوضة بطلب جديد معتمد تعيدها للقائمة (العبرة بآخر طلب)
        var again = await owner.Post("/approvals/check", new { documentType = "sales", actionType = "create", documentId = rejected.Invoice,
            documentNumber = "again", documentAmount = 115 });
        Assert.Equal(200, (await manager.Post($"/approvals/{again.Data!["request"]!["id"].S()}/approve", new { comment = "بعد التعديل" })).Status);
        Assert.Contains(rejected.Invoice, await ListedInvoiceIdsAsync(owner));
    }

    [Fact]
    public async Task Invoice_list_stays_fast_with_thousands_of_invoices_and_approval_requests()
    {
        const int invoices = 5000, foreignRequests = 20000;
        var owner = await NewTenantAsync();
        var product = await SeedProductAsync(owner);
        await PolicyAsync(owner, "general_manager");
        var seed = await DraftWithRequestAsync(owner, product); // معلّقة: مخفية

        await using (var db = new SqlConnection(Factory.ConnectionString))
        {
            await db.OpenAsync();
            // فواتير مسودة مستنسخة بأرقام فريدة
            await CloneAsync(db, "SalesInvoice", seed.Invoice, Numbers(invoices),
                new() { ["Id"] = "NEWID()", ["InvoiceNumber"] = "CONCAT('BULK-', x.n)" });
            // طلب على كل فاتورة مستنسخة بحالة دورية: معلّق، مرفوض، معاد
            await CloneAsync(db, "ApprovalRequest", seed.Request,
                "(SELECT i.Id AS DocId, CAST(SUBSTRING(i.InvoiceNumber, 6, 10) AS int) AS n FROM SalesInvoice i WHERE i.InvoiceNumber LIKE 'BULK-%') x",
                new() { ["Id"] = "NEWID()", ["DocumentId"] = "x.DocId",
                        ["Status"] = "CASE x.n % 3 WHEN 0 THEN 'pending' WHEN 1 THEN 'rejected' ELSE 'returned' END",
                        ["CreatedAt"] = "DATEADD(second, x.n, src.CreatedAt)" });
            // طلبات كثيرة على مستندات أخرى تكبّر الجدول
            await CloneAsync(db, "ApprovalRequest", seed.Request, Numbers(foreignRequests),
                new() { ["Id"] = "NEWID()", ["DocumentId"] = "NEWID()" });
        }

        var expected = Enumerable.Range(1, invoices).Count(n => n % 3 == 2); // المعادة فقط

        var list = await TimeAsync(owner, "/sales-invoices?pageSize=50");
        Assert.Equal(expected, (int)list.Data!["totalCount"].D());
        var load = await TimeAsync(owner, "/sales-invoices/load?take=50&skip=0&requireTotalCount=true");
        Assert.Equal(expected, load.Body!["totalCount"]!.GetValue<int>());
    }

    /// <summary>متوسط زمن 5 طلبات بعد طلب تسخين؛ يُطبع ويُشترط أن يبقى دون ثانية ونصف.</summary>
    private async Task<Res> TimeAsync(Client api, string path)
    {
        var res = await api.Get(path);
        Assert.Equal(200, res.Status);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) res = await api.Get(path);
        var avg = sw.ElapsedMilliseconds / 5.0;
        _out.WriteLine($"{path}: {avg:0} ms");
        Assert.True(avg < 1500, $"{path} took {avg:0} ms");
        return res;
    }

    private static string Numbers(int count)
        => $"(SELECT TOP ({count}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x";

    /// <summary>يستنسخ صفاً موجوداً لكل صف من source (بالاسم x)، مع تعبيرات تستبدل أعمدة بعينها.</summary>
    private static async Task CloneAsync(SqlConnection db, string table, string sourceId, string source, Dictionary<string, string> overrides)
    {
        var columns = new List<string>();
        await using (var cmd = new SqlCommand(
            "SELECT c.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id " +
            "WHERE c.object_id = OBJECT_ID(@t) AND c.is_computed = 0 AND c.is_identity = 0 AND t.name NOT IN ('timestamp', 'rowversion')", db))
        {
            cmd.Parameters.AddWithValue("@t", table);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) columns.Add(r.GetString(0));
        }
        var targets = string.Join(", ", columns.Select(c => $"[{c}]"));
        var values = string.Join(", ", columns.Select(c => overrides.TryGetValue(c, out var e) ? e : $"src.[{c}]"));
        await using var insert = new SqlCommand($"INSERT INTO [{table}] ({targets}) SELECT {values} FROM [{table}] src CROSS JOIN {source} WHERE src.Id = @id", db)
        {
            CommandTimeout = 300,
        };
        insert.Parameters.AddWithValue("@id", Guid.Parse(sourceId));
        await insert.ExecuteNonQueryAsync();
    }
}
