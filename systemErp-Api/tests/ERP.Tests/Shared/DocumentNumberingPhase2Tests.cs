using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// ترقيم المستندات (المرحلة الثانية): العقود والاتفاقيات وبيانات التسليم وسندات الاستلام ومرتجعاتها، الجرد والتحويلات وطلبات الشراء،
/// وأوامر شراء السيارات. لكل نوع: التلقائي بإعداده، واليدوي إلزامي وفريد، وسند الاستلام بعدّاد مستقل عن بيان التسليم.
/// </summary>
[Collection("api")]
public class DocumentNumberingPhase2Tests : TestBase
{
    public DocumentNumberingPhase2Tests(ErpFactory f) : base(f) { }

    private static object Manual(string prefix = "X-") => new { mode = "manual", prefix, padding = 6, resetYearly = false, isActive = true };
    private static object Auto(string prefix, int padding = 4) => new { mode = "auto", prefix, padding, resetYearly = false, isActive = true };

    private static object Note(string type, Guid item, string? number = null) => new
    {
        type, deliveryNumber = number ?? "", partyName = "طرف", date = DateTime.UtcNow,
        items = new[] { new { itemId = item, itemName = "بند", contractQty = 10, deliveredQty = 10 } },
    };

    private static async Task<Guid> NewWarehouseAsync(Client api, string code)
    {
        var w = await api.Post("/warehouses", new { code, nameAr = "مستودع " + code, nameEn = code, location = "الرياض", status = "active" });
        Assert.Equal(201, w.Status);
        return w.Data!["id"].G();
    }

    private static async Task<Guid> DefaultWarehouseAsync(Client api)
        => (await api.Get("/warehouses")).Data!["items"]!.AsArray().First(w => w!["isDefault"]!.GetValue<bool>())!["id"].G();

    [Fact]
    public async Task Phase_two_types_are_listed_and_goods_receipts_have_their_own_series()
    {
        var api = await NewTenantAsync();
        var keys = (await api.Get("/document-numbering")).Data!.AsArray().Select(t => t!["key"].S()).ToHashSet();
        foreach (var key in new[] { "material_requisition", "commercial_contract", "agreement", "delivery_note", "goods_receipt", "delivery_return",
                     "inventory_count", "stock_transfer", "car_procurement_order", "car_sales_contract" })
            Assert.Contains(key, keys);

        var item = await SeedProductAsync(api, stock: 50);
        Assert.Equal(200, (await api.Put("/document-numbering/delivery_note", Auto("DLV-"))).Status);
        Assert.Equal(200, (await api.Put("/document-numbering/goods_receipt", Auto("REC-"))).Status);

        // بيان التسليم وسند الاستلام: بادئة وعدّاد لكل منهما (كانا عدّاداً واحداً ببادئة أول مستند)
        Assert.Equal("DLV-0001", (await api.Post("/deliverynotes", Note("sales_delivery", item))).Data!["deliveryNumber"].S());
        Assert.Equal("REC-0001", (await api.Post("/deliverynotes", Note("purchase_delivery", item))).Data!["deliveryNumber"].S());
        Assert.Equal("DLV-0002", (await api.Post("/deliverynotes", Note("sales_delivery", item))).Data!["deliveryNumber"].S());
    }

    [Fact]
    public async Task Manual_numbers_for_contracts_agreements_delivery_documents_and_requisitions()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 50);
        foreach (var key in new[] { "commercial_contract", "agreement", "delivery_note", "goods_receipt", "delivery_return", "material_requisition" })
            Assert.Equal(200, (await api.Put($"/document-numbering/{key}", Manual())).Status);

        // بيان تسليم: الرقم إلزامي وفريد في الجدول (بيع وشراء معاً)
        Assert.Equal(400, (await api.Post("/deliverynotes", Note("sales_delivery", item))).Status);
        var note = await api.Post("/deliverynotes", Note("sales_delivery", item, "DN-77"));
        Assert.Equal("DN-77", note.Data!["deliveryNumber"].S());
        Assert.Equal(409, (await api.Post("/deliverynotes", Note("purchase_delivery", item, "DN-77"))).Status);
        Assert.Equal("GR-5", (await api.Post("/deliverynotes", Note("purchase_delivery", item, "GR-5"))).Data!["deliveryNumber"].S());

        // مرتجع بيان التسليم
        object Return(string? n) => new { type = "sales_delivery_return", returnNumber = n ?? "", deliveryNoteId = note.Data["id"].S(), returnReason = "تالف",
            items = new[] { new { itemId = item, quantity = 1 } } };
        Assert.Equal(400, (await api.Post("/deliverynotes/returns", Return(null))).Status);
        var ret = await api.Post("/deliverynotes/returns", Return("DR-9"));
        Assert.True(ret.Status is 200 or 201, ret.Body?.ToJsonString());
        Assert.Equal("DR-9", ret.Data!["returnNumber"].S());

        // اتفاقية
        object Agreement(string? n) => new { agreementNumber = n ?? "", type = "sales", partyName = "عميل", startDate = DateTime.UtcNow.AddDays(-1), status = "active",
            items = new[] { new { id = Guid.Empty, itemId = item, itemName = "منتج", quantity = 10, unitPrice = 100, minQuantity = 1, maxQuantity = 6 } } };
        Assert.Equal(400, (await api.Post("/agreements", Agreement(null))).Status);
        var agr = await api.Post("/agreements", Agreement("AG-1"));
        Assert.Equal(201, agr.Status);
        Assert.Equal("AG-1", agr.Data!["agreementNumber"].S());
        Assert.Equal(409, (await api.Post("/agreements", Agreement("AG-1"))).Status);

        // عقد: الرقم اليدوي ولا يتغير بالتعديل
        var customer = (await SeedCustomerAsync(api)).Item1;
        object Contract(string? n) => new { contractNumber = n ?? "", title = "عقد صيانة", contractType = "maintenance", partyName = "عميل", partyId = customer, contractValue = 1000, vatRate = 15,
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(12),
            milestones = new[] { new { title = "دفعة 1", percentage = 50 }, new { title = "دفعة 2", percentage = 50 } }, clauses = Array.Empty<object>() };
        Assert.Equal(400, (await api.Post("/commercialcontracts", Contract(null))).Status);
        var contract = await api.Post("/commercialcontracts", Contract("C-2026/1"));
        Assert.True(contract.Status is 200 or 201, contract.Body?.ToJsonString());
        Assert.Equal("C-2026/1", contract.Data!["contractNumber"].S());
        Assert.Equal(409, (await api.Post("/commercialcontracts", Contract("C-2026/1"))).Status);
        Assert.Equal(400, (await api.Post("/commercialcontracts", Contract("بالعربي"))).Status); // صيغة غير مقبولة

        // طلب شراء
        object Requisition(string? n) => new { requisitionNumber = n ?? "", requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(5), department = "المشتريات",
            requestedBy = "أحمد", priority = "high", status = "draft",
            items = new[] { new { itemId = item, itemName = "منتج", sku = "P1", unit = "PCS", requestedQuantity = 5, estimatedCost = 30 } } };
        Assert.Equal(400, (await api.Post("/materialrequisitions", Requisition(null))).Status);
        var req = await api.Post("/materialrequisitions", Requisition("PR-3"));
        Assert.True(req.Status is 200 or 201, req.Body?.ToJsonString());
        Assert.Equal("PR-3", req.Data!["requisitionNumber"].S());
    }

    [Fact]
    public async Task Manual_numbers_for_inventory_counts_transfers_and_car_purchase_orders()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 20);
        foreach (var key in new[] { "inventory_count", "stock_transfer", "car_procurement_order" })
            Assert.Equal(200, (await api.Put($"/document-numbering/{key}", Manual())).Status);

        // جرد: الرقم اليدوي ولا يتغير بالتعديل
        object Count(string? n) => new { countNumber = n, scope = "items", countType = "full", countDate = DateTime.UtcNow,
            lines = new[] { new { itemId = item, countedQuantity = 20 } } };
        Assert.Equal(400, (await api.Post("/inventorycounts", Count(null))).Status);
        var count = await api.Post("/inventorycounts", Count("CNT-1"));
        Assert.True(count.Status is 200 or 201, count.Body?.ToJsonString());
        Assert.Equal("CNT-1", count.Data!["countNumber"].S());
        Assert.Equal(409, (await api.Post("/inventorycounts", Count("CNT-1"))).Status);
        var edited = await api.Put($"/inventorycounts/{count.Data["id"].S()}", Count("CNT-CHANGED"));
        Assert.True(edited.Status == 200, edited.Body?.ToJsonString());
        Assert.Equal("CNT-1", edited.Data!["countNumber"].S());

        // تحويل مخزني
        var main = await DefaultWarehouseAsync(api);
        var branch = await NewWarehouseAsync(api, "BR");
        object Transfer(string? n) => new { transferNumber = n, fromWarehouseId = main, toWarehouseId = branch, items = new[] { new { itemId = item, quantity = 1 } } };
        Assert.Equal(400, (await api.Post("/stocktransfers", Transfer(null))).Status);
        var transfer = await api.Post("/stocktransfers", Transfer("T-1"));
        Assert.Equal(200, transfer.Status);
        Assert.Equal("T-1", transfer.Data!["transferNumber"].S());
        Assert.Equal(409, (await api.Post("/stocktransfers", Transfer("T-1"))).Status);

        // أمر شراء سيارات
        var (supplier, _) = await SeedSupplierAsync(api);
        object Order(string? n) => new { orderNumber = n ?? "", supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 1, unitPrice = 80000 } } };
        Assert.Equal(400, (await api.Post("/carprocurementorders", Order(null))).Status);
        var order = await api.Post("/carprocurementorders", Order("CAR-PO-1"));
        Assert.Equal(201, order.Status);
        Assert.Equal("CAR-PO-1", order.Data!["orderNumber"].S());
        Assert.Equal(409, (await api.Post("/carprocurementorders", Order("CAR-PO-1"))).Status);
    }
}
