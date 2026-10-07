using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>عمليات التعديل/الحذف/القراءة بالمعرّف على المستندات التي لم تكن مكتملة CRUD.</summary>
[Collection("api")]
public class CrudCompletenessTests : TestBase
{
    public CrudCompletenessTests(ErpFactory f) : base(f) { }

    [Fact]
    public async Task Voucher_update_reverses_the_old_journal_and_reposts_with_the_same_number()
    {
        var api = await NewTenantAsync();
        var (customer, account) = await SeedCustomerAsync(api);
        var product = await SeedProductAsync(api);
        await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "credit", partyId = customer, items = new[] { new { itemId = product, quantity = 5, unitPrice = 100, vatRate = 15 } } });
        var v = await api.Post("/vouchers", new { type = "receipt", amount = 100, partyName = "عميل", partyAccountCode = account, treasuryAccountCode = "1111", paymentMethod = "cash" });
        var id = v.Data!["id"].S();

        var upd = await api.Put($"/vouchers/{id}", new { type = "receipt", amount = 300, partyName = "عميل", partyAccountCode = account, treasuryAccountCode = "1111", paymentMethod = "cash" });
        Assert.Equal(200, upd.Status);
        Assert.Equal(v.Data["voucherNumber"].S(), upd.Data!["voucherNumber"].S());
        Assert.Equal(300, upd.Data["amount"].D());
        Assert.Equal(575 - 300, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D());
        Assert.Equal(409, (await api.Put($"/vouchers/{id}", new { type = "payment", amount = 10, partyName = "x", partyAccountCode = account, treasuryAccountCode = "1111", paymentMethod = "cash" })).Status);
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Draft_invoices_can_be_updated_and_posted_but_a_posted_invoice_is_locked()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        object Body(decimal qty, string status) => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status, items = new[] { new { itemId = product, quantity = qty, unitPrice = 100, vatRate = 15 } } };

        var draft = await api.Post("/invoices", Body(1, "draft"));
        var id = draft.Data!["id"].S(); var number = draft.Data["invoiceNumber"].S();
        var upd = await api.Put($"/invoices/{id}", Body(3, "draft"));
        Assert.Equal(200, upd.Status);
        Assert.Equal(number, upd.Data!["invoiceNumber"].S()); Assert.Equal(345, upd.Data["grandTotal"].D()); Assert.Single(upd.Data["items"]!.AsArray());
        Assert.Equal(10, (await api.Get($"/products/{product}")).Data!["currentStock"].D()); // لا أثر بعد

        var posted = await api.Put($"/invoices/{id}", Body(2, "posted"));
        Assert.Equal("posted", posted.Data!["status"].S()); Assert.Equal(230, posted.Data["grandTotal"].D());
        Assert.Equal(8, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.StartsWith("SINV-", posted.Data["invoiceNumber"].S()); // صُرف الرقم المتسلسل عند الترحيل
        // المرحّلة مستند صادر: لا تُعدَّل ولا تعود مسودة، وأثرها باقٍ
        Assert.Equal(409, (await api.Put($"/invoices/{id}", Body(1, "draft"))).Status);
        Assert.Equal(409, (await api.Put($"/invoices/{id}", Body(5, "posted"))).Status);
        Assert.Equal(8, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(404, (await api.Put($"/invoices/{Guid.NewGuid()}", Body(1, "draft"))).Status);
    }

    [Fact]
    public async Task Invoice_linked_to_another_document_cannot_be_updated_or_deleted()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var linked = await api.Post("/invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
            referenceType = "car_sales_contract", referenceId = Guid.NewGuid(),
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } },
        });
        var id = linked.Data!["id"].S();

        Assert.Equal(409, (await api.Put($"/invoices/{id}", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } },
        })).Status);
        Assert.Equal(409, (await api.Delete($"/invoices/{id}")).Status);
    }

    [Fact]
    public async Task Delivery_note_update_delete_and_return_removal_keep_quantities_consistent()
    {
        var api = await NewTenantAsync();
        var item = Guid.NewGuid();
        var note = await api.Post("/deliverynotes", new { type = "sales_delivery", partyName = "عميل", date = DateTime.UtcNow, items = new[] { new { itemId = item, itemName = "بند", contractQty = 10, deliveredQty = 10 } } });
        var id = note.Data!["id"].S();

        var upd = await api.Put($"/deliverynotes/{id}", new { type = "sales_delivery", partyName = "عميل معدّل", date = DateTime.UtcNow, items = new[] { new { itemId = item, itemName = "بند", contractQty = 10, deliveredQty = 8 } } });
        Assert.Equal(200, upd.Status); Assert.Equal("عميل معدّل", upd.Data!["partyName"].S()); Assert.Equal(8, upd.Data["items"]![0]!["deliveredQty"].D());

        var ret = await api.Post("/deliverynotes/returns", new { type = "sales_delivery_return", deliveryNoteId = id, items = new[] { new { itemId = item, quantity = 3 } } });
        Assert.Equal("partially_returned", (await api.Get($"/deliverynotes/{id}")).Data!["status"].S());
        Assert.Equal(409, (await api.Delete($"/deliverynotes/{id}")).Status); // عليه مرتجع
        Assert.Equal(200, (await api.Get($"/deliverynotes/returns/{ret.Data!["id"].S()}")).Status);

        Assert.Equal(200, (await api.Delete($"/deliverynotes/returns/{ret.Data["id"].S()}")).Status);
        var after = (await api.Get($"/deliverynotes/{id}")).Data!;
        Assert.Equal("delivered", after["status"].S()); Assert.Equal(0, after["items"]![0]!["returnedQty"].D());
        Assert.Equal(200, (await api.Delete($"/deliverynotes/{id}")).Status);
        Assert.Equal(404, (await api.Get($"/deliverynotes/{id}")).Status);
    }

    [Fact]
    public async Task Notifications_can_be_read_and_deleted_individually_and_only_by_their_owner()
    {
        var api = await NewTenantAsync();
        var me = (await api.Get("/auth/me")).Data!["id"].S();
        var n = await api.Post("/notifications", new { recipientUserId = me, title = "تنبيه", body = "نص", type = "system_alert" });
        var id = n.Data!["id"].S();
        Assert.Equal(200, (await api.Get($"/notifications/{id}")).Status);
        var other = await NewTenantAsync();
        Assert.Equal(404, (await other.Get($"/notifications/{id}")).Status);
        Assert.Equal(200, (await api.Delete($"/notifications/{id}")).Status);
        Assert.Equal(404, (await api.Get($"/notifications/{id}")).Status);
    }

    [Fact]
    public async Task Pending_approval_request_can_be_withdrawn_by_its_requester()
    {
        var api = await NewTenantAsync();
        var policy = await api.Post("/ApprovalPolicies", new { nameAr = "اعتماد المبالغ الكبيرة", documentType = "sales", actionType = "create", minAmountTrigger = 1000, isActive = true,
            steps = new[] { new { level = 1, approverRole = "general_manager", titleAr = "المدير العام" } } });
        Assert.Equal(201, policy.Status);
        var check = await api.Post("/approvals/check", new { documentType = "sales", actionType = "create", documentId = Guid.NewGuid(), documentNumber = "S-1", documentAmount = 5000 });
        Assert.True(check.Data!["approvalRequired"]!.GetValue<bool>());
        var id = check.Data["request"]!["id"].S();

        var cancelled = await api.Delete($"/approvals/{id}");
        Assert.Equal(200, cancelled.Status); Assert.Equal("cancelled", cancelled.Data!["status"].S());
        Assert.Equal(409, (await api.Delete($"/approvals/{id}")).Status);
    }

    [Fact]
    public async Task Draft_invoice_waiting_for_approval_cannot_be_posted_until_approved()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await api.Post("/ApprovalPolicies", new { nameAr = "اعتماد المبيعات", documentType = "sales", actionType = "create", minAmountTrigger = 1, isActive = true,
            steps = new[] { new { level = 1, approverRole = "owner", titleAr = "المالك" } } });
        var draft = await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } });
        var id = draft.Data!["id"].S();
        var check = await api.Post("/approvals/check", new { documentType = "sales", actionType = "create", documentId = id, documentNumber = draft.Data["invoiceNumber"].S(), documentAmount = 115 });
        Assert.True(check.Data!["approvalRequired"]!.GetValue<bool>());

        Assert.Equal(409, (await api.Post($"/invoices/{id}/post")).Status);
        Assert.Equal(200, (await api.Post($"/approvals/{check.Data["request"]!["id"].S()}/approve", new { comment = "ok" })).Status);
        Assert.Equal("posted", (await api.Post($"/invoices/{id}/post")).Data!["status"].S());
    }

    [Fact]
    public async Task Stock_movement_and_loyalty_can_be_read_by_id()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var movement = (await api.Get($"/stockmovements?itemId={product}")).Data!["items"]![0]!["id"].S();
        Assert.Equal(200, (await api.Get($"/stockmovements/{movement}")).Status);
        Assert.Equal(404, (await api.Get($"/stockmovements/{Guid.NewGuid()}")).Status);
        Assert.Equal(404, (await api.Get($"/pos/loyalty/{Guid.NewGuid()}")).Status);
    }
}
