using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// شراء التشغيلات بأسعار مختلفة وإعدادات التتبّع الأربعة: التشغيلة تعريف واحد (الصنف + الرقم + الصلاحية) يُستقبل مرات بأسعار مختلفة،
/// كل استلام حركة مستقلة مرتبطة بفاتورته وتكلفتها، والتكلفة بطريقة المنشأة (متوسط متحرك أو FIFO) لا بالتشغيلة.
/// </summary>
[Collection("api")]
public class ItemBatchPurchaseTests : TestBase
{
    public ItemBatchPurchaseTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static async Task<Guid> ProductAsync(Client api, string sku, bool batches, bool expiry)
    {
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        var p = await api.Post("/products", new
        {
            sku, nameAr = "صنف " + sku, category = "C1", unit = "PCS", sellingPrice = 100, vatRate = 15,
            trackBatches = batches, trackExpiry = expiry, expiryAlertDays = 30,
        });
        Assert.True(p.Status == 201, p.Body?.ToJsonString());
        return p.Data!["id"].G();
    }

    private static Task<Res> PurchaseAsync(Client api, Guid supplier, Guid item, decimal qty, decimal price, string? batch, DateTime? expiry,
        Guid? warehouse = null)
        => api.Post("/purchase-invoices", new
        {
            kind = "purchase", invoiceType = "simplified", paymentMethod = "credit", partyId = supplier, warehouseId = warehouse,
            items = new[] { new { itemId = item, quantity = qty, unitPrice = price, vatRate = 15, batchNumber = batch, expiryDate = expiry } },
        });

    private static Task<Res> SaleAsync(Client api, Guid item, decimal qty, Guid? batch = null)
        => api.Post("/sales-invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            items = new[] { new { itemId = item, quantity = qty, unitPrice = 100, vatRate = 15, itemBatchId = batch } },
        });

    private static async Task<JsonArray> BatchesAsync(Client api, Guid item, Guid? warehouse = null)
        => (await api.Get($"/products/{item}/batches?includeEmpty=true" + (warehouse is { } w ? $"&warehouseId={w}" : ""))).Data!.AsArray();

    private static async Task<JsonObject> ProductOfAsync(Client api, Guid item) => (await api.Get($"/products/{item}")).Data!.AsObject();

    private static void Ok201(Res r) => Assert.True(r.Status == 201, r.Body?.ToJsonString());

    private static object Policy(string method)
        => new { method, recalculateOnNewPurchase = true, includeFreightAndCustoms = false, negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" };

    [Theory]
    [InlineData("moving_average", 5.3333)] // (100×5 + 50×6) ÷ 150 لكل وحدة مصروفة
    [InlineData("fifo", 5.1667)]           // 100×5 من الاستلام الأول + 20×6 من الثاني ÷ 120
    public async Task Rebuying_the_same_batch_at_a_different_price_keeps_one_batch_two_receipts_and_the_costing_method(string method, decimal saleUnitCost)
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/costing/policy", Policy(method))).Status);
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "B-PRICE", batches: true, expiry: true);
        var expiry = Today.AddDays(365);

        var first = await PurchaseAsync(api, supplier, item, 100, 5, "B001", expiry);
        Ok201(first);
        var second = await PurchaseAsync(api, supplier, item, 50, 6, "B001", expiry);
        Ok201(second);

        // تشغيلة واحدة بالكمية كلها
        var batch = Assert.Single(await BatchesAsync(api, item))!;
        Assert.Equal("B001", batch["batchNumber"].S());
        Assert.Equal(150, batch["quantity"].D());
        Assert.Equal(expiry, DateTime.Parse(batch["expiryDate"].S()).Date);

        // كل استلام حركة مستقلة بتكلفته ومرتبطة بفاتورته، ولا يُستبدل الأول
        var receipts = (await api.Get($"/stockmovements?itemId={item}")).Data!["items"]!.AsArray()
            .Where(m => m!["type"].S() == "in_purchase").ToList();
        Assert.Equal(2, receipts.Count);
        Assert.Contains(receipts, m => m!["sourceId"].G() == first.Data!["id"].G() && m["unitCost"].D() == 5 && m["quantity"].D() == 100);
        Assert.Contains(receipts, m => m!["sourceId"].G() == second.Data!["id"].G() && m["unitCost"].D() == 6 && m["quantity"].D() == 50);
        Assert.All(receipts, m => Assert.StartsWith("B001", m!["batchSummary"].S()));

        // متوسط الصنف بعد الاستلامين: (500 + 300) ÷ 150 في الطريقتين قبل الصرف
        Assert.Equal(5.3333m, (await ProductOfAsync(api, item))["averageCost"].D());

        var sale = await SaleAsync(api, item, 120);
        Ok201(sale);
        Assert.Equal(saleUnitCost, sale.Data!["items"]![0]!["unitCost"].D());
        Assert.Equal(30, Assert.Single(await BatchesAsync(api, item))!["quantity"].D());
    }

    [Fact]
    public async Task Same_batch_number_with_a_different_expiry_is_rejected_without_any_effect()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "B-EXP", batches: true, expiry: true);
        Ok201(await PurchaseAsync(api, supplier, item, 10, 5, "L1", Today.AddDays(100)));

        var clash = await PurchaseAsync(api, supplier, item, 5, 5, "L1", Today.AddDays(120));
        Assert.Equal(409, clash.Status);
        Assert.Contains("L1", clash.Body!["message"].S());
        Assert.Equal(10, Assert.Single(await BatchesAsync(api, item))!["quantity"].D());
        Assert.Equal(10, (await ProductOfAsync(api, item))["currentStock"].D());
    }

    [Fact]
    public async Task Batch_only_item_needs_a_number_but_no_expiry()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "B-ONLY", batches: true, expiry: false);

        Assert.Equal(400, (await PurchaseAsync(api, supplier, item, 5, 5, null, null)).Status);
        Ok201(await PurchaseAsync(api, supplier, item, 5, 5, "N1", null));
        Ok201(await PurchaseAsync(api, supplier, item, 3, 7, "N1", null));
        var batch = Assert.Single(await BatchesAsync(api, item))!;
        Assert.Equal(8, batch["quantity"].D());
        Assert.Null(batch["expiryDate"]);
    }

    [Fact]
    public async Task Expiry_only_item_needs_an_expiry_and_groups_receipts_by_that_date_without_a_number()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "E-ONLY", batches: false, expiry: true);
        Assert.False((await ProductOfAsync(api, item))["trackBatches"]!.GetValue<bool>());
        Assert.True((await ProductOfAsync(api, item))["trackExpiry"]!.GetValue<bool>());

        var missing = await PurchaseAsync(api, supplier, item, 5, 5, null, null);
        Assert.Equal(400, missing.Status);
        Assert.Contains("الصلاحية", missing.Body!["message"].S());

        var near = Today.AddDays(30);
        var far = Today.AddDays(200);
        Ok201(await PurchaseAsync(api, supplier, item, 4, 5, null, far));
        Ok201(await PurchaseAsync(api, supplier, item, 6, 5, null, near));
        Ok201(await PurchaseAsync(api, supplier, item, 2, 6, null, near)); // التاريخ نفسه بسعر آخر: التشغيلة نفسها

        var batches = await BatchesAsync(api, item);
        Assert.Equal(2, batches.Count);
        var nearBatch = batches.First(b => DateTime.Parse(b!["expiryDate"].S()).Date == near)!;
        Assert.Equal(8, nearBatch["quantity"].D());
        Assert.Equal($"EXP-{near:yyyyMMdd}", nearBatch["batchNumber"].S());

        // البيع بالأقرب انتهاءً
        Ok201(await SaleAsync(api, item, 9));
        var left = await BatchesAsync(api, item);
        Assert.Equal(0, left.First(b => DateTime.Parse(b!["expiryDate"].S()).Date == near)!["quantity"].D());
        Assert.Equal(3, left.First(b => DateTime.Parse(b!["expiryDate"].S()).Date == far)!["quantity"].D());
    }

    [Fact]
    public async Task Expiry_only_receipt_without_a_number_never_joins_a_supplier_lot_with_the_same_date()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "E-LOTS", batches: false, expiry: true);
        var date = Today.AddDays(90);

        // تشغيلتان فعليتان بأرقام المورد وتاريخ واحد: منفصلتان
        Ok201(await PurchaseAsync(api, supplier, item, 5, 5, "LOT-A", date));
        Ok201(await PurchaseAsync(api, supplier, item, 7, 6, "LOT-B", date));
        // بلا رقم: تشغيلة التاريخ المولَّدة، لا LOT-A
        Ok201(await PurchaseAsync(api, supplier, item, 1, 6, null, date));
        Ok201(await PurchaseAsync(api, supplier, item, 2, 6, null, date));

        var batches = (await BatchesAsync(api, item)).ToDictionary(b => b!["batchNumber"].S(), b => b!["quantity"].D());
        Assert.Equal(3, batches.Count);
        Assert.Equal(5, batches["LOT-A"]);
        Assert.Equal(7, batches["LOT-B"]);
        Assert.Equal(3, batches[$"EXP-{date:yyyyMMdd}"]);
    }

    [Fact]
    public async Task Expiry_only_count_lines_find_the_batch_by_date_and_a_new_date_becomes_a_new_batch()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "E-CNT", batches: false, expiry: true);
        var near = Today.AddDays(40);
        var found = Today.AddDays(400);
        Ok201(await PurchaseAsync(api, supplier, item, 5, 5, null, near));

        var count = await api.Post("/inventorycounts", new
        {
            scope = "items", countType = "full", countDate = DateTime.UtcNow,
            lines = new object[]
            {
                new { itemId = item, expiryDate = near, countedQuantity = 3m },  // عجز 2 في تشغيلة التاريخ القائم
                new { itemId = item, expiryDate = found, countedQuantity = 2m }, // تاريخ معثور عليه: تشغيلة جديدة
            },
        });
        Assert.True(count.Status == 201, count.Body?.ToJsonString());
        var lines = count.Data!["lines"]!.AsArray();
        Assert.Equal(5, lines.First(l => DateTime.Parse(l!["expiryDate"].S()).Date == near)!["systemQuantity"].D());
        Assert.Equal(0, lines.First(l => DateTime.Parse(l!["expiryDate"].S()).Date == found)!["systemQuantity"].D());

        var approval = await api.Post($"/inventorycounts/{count.Data["id"].S()}/submit");
        Assert.True(approval.Status == 200, approval.Body?.ToJsonString());
        Assert.Equal(200, (await api.Post($"/inventorycountapprovals/{approval.Data!["id"].S()}/approve", new { })).Status);

        var batches = await BatchesAsync(api, item);
        Assert.Equal(3, batches.First(b => DateTime.Parse(b!["expiryDate"].S()).Date == near)!["quantity"].D());
        var created = batches.First(b => DateTime.Parse(b!["expiryDate"].S()).Date == found)!;
        Assert.Equal(2, created["quantity"].D());
        Assert.Equal($"EXP-{found:yyyyMMdd}", created["batchNumber"].S());
    }

    [Fact]
    public async Task Batch_and_expiry_item_needs_both()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "BOTH", batches: true, expiry: true);
        Assert.Equal(400, (await PurchaseAsync(api, supplier, item, 5, 5, null, Today.AddDays(90))).Status);
        Assert.Equal(400, (await PurchaseAsync(api, supplier, item, 5, 5, "X1", null)).Status);
        Ok201(await PurchaseAsync(api, supplier, item, 5, 5, "X1", Today.AddDays(90)));
        Assert.Single(await BatchesAsync(api, item));
    }

    [Fact]
    public async Task Untracked_item_ignores_batch_fields_and_keeps_its_old_behaviour()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "PLAIN", batches: false, expiry: false);
        Ok201(await PurchaseAsync(api, supplier, item, 5, 5, "IGNORED", Today.AddDays(10)));
        Ok201(await PurchaseAsync(api, supplier, item, 5, 5, null, null));
        Assert.Empty(await BatchesAsync(api, item));
        Assert.Equal(10, (await ProductOfAsync(api, item))["currentStock"].D());
        Ok201(await SaleAsync(api, item, 4));
        Assert.Equal(6, (await ProductOfAsync(api, item))["currentStock"].D());
    }

    [Fact]
    public async Task Same_batch_in_two_warehouses_is_one_batch_with_a_balance_per_warehouse()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "TWO-WH", batches: true, expiry: true);
        var main = (await api.Get("/warehouses")).Data!["items"]!.AsArray().First(w => w!["isDefault"]!.GetValue<bool>())!["id"].G();
        var branch = (await api.Post("/warehouses", new { code = "BR", nameAr = "فرع", nameEn = "BR", location = "جدة", status = "active" })).Data!["id"].G();
        var expiry = Today.AddDays(180);

        Ok201(await PurchaseAsync(api, supplier, item, 7, 5, "W1", expiry, main));
        Ok201(await PurchaseAsync(api, supplier, item, 3, 6, "W1", expiry, branch));

        var all = Assert.Single(await BatchesAsync(api, item))!;
        Assert.Equal(10, all["quantity"].D());
        Assert.Equal(7, Assert.Single(await BatchesAsync(api, item, main))!["quantity"].D());
        Assert.Equal(3, Assert.Single(await BatchesAsync(api, item, branch))!["quantity"].D());
    }

    [Fact]
    public async Task Sale_from_a_chosen_batch_takes_only_that_batch_and_never_more_than_its_balance()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "PICK", batches: true, expiry: true);
        Ok201(await PurchaseAsync(api, supplier, item, 5, 5, "EARLY", Today.AddDays(20)));
        Ok201(await PurchaseAsync(api, supplier, item, 5, 5, "LATE", Today.AddDays(300)));
        var late = (await BatchesAsync(api, item)).First(b => b!["batchNumber"].S() == "LATE")!["id"].G();

        // اختيار يدوي يتجاوز FEFO
        var sale = await SaleAsync(api, item, 2, late);
        Ok201(sale);
        Assert.Equal($"LATE ({Today.AddDays(300):yyyy-MM-dd})", sale.Data!["items"]![0]!["batchNumber"].S());
        var after = (await BatchesAsync(api, item)).ToDictionary(b => b!["batchNumber"].S(), b => b!["quantity"].D());
        Assert.Equal(5, after["EARLY"]);
        Assert.Equal(3, after["LATE"]);

        // أكثر من رصيد التشغيلة المختارة: مرفوض وإن كفى رصيد الصنف
        Assert.Equal(409, (await SaleAsync(api, item, 4, late)).Status);
        Assert.Equal(8, (await ProductOfAsync(api, item))["currentStock"].D());
    }

    [Fact]
    public async Task Concurrent_purchases_of_a_new_batch_never_create_it_twice()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await ProductAsync(api, "RACE", batches: true, expiry: true);
        var expiry = Today.AddDays(90);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PurchaseAsync(api, supplier, item, 10, 5, "RACE-1", expiry)));
        Assert.All(results, r => Assert.True(r.Status is 201 or 409, r.Body?.ToJsonString()));
        var succeeded = results.Count(r => r.Status == 201);
        Assert.True(succeeded >= 1);

        var batch = Assert.Single(await BatchesAsync(api, item))!;
        Assert.Equal(10 * succeeded, batch["quantity"].D());
        Assert.Equal(10 * succeeded, (await ProductOfAsync(api, item))["currentStock"].D());

        // إعادة المحاولة بعد التعارض تعود للتشغيلة نفسها
        Ok201(await PurchaseAsync(api, supplier, item, 10, 5, "RACE-1", expiry));
        Assert.Equal(10 * (succeeded + 1), Assert.Single(await BatchesAsync(api, item))!["quantity"].D());
    }

    [Fact]
    public async Task Correcting_dates_of_a_batch_with_movements_needs_the_correction_permission_and_is_audited()
    {
        var owner = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(owner);
        var item = await ProductAsync(owner, "FIX", batches: true, expiry: true);
        Ok201(await PurchaseAsync(owner, supplier, item, 5, 5, "F1", Today.AddDays(60)));
        var batchId = Assert.Single(await BatchesAsync(owner, item))!["id"].G();

        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مستخدم", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        var corrected = Today.AddDays(75);
        // الملاحظات وحدها لا تمس التاريخ: مسموحة
        Assert.Equal(200, (await rep.Put($"/products/batches/{batchId}", new { expiryDate = Today.AddDays(60), notes = "ملاحظة" })).Status);
        Assert.Equal(403, (await rep.Put($"/products/batches/{batchId}", new { expiryDate = corrected, notes = "تصحيح" })).Status);
        Assert.Equal(Today.AddDays(60), DateTime.Parse(Assert.Single(await BatchesAsync(owner, item))!["expiryDate"].S()).Date);

        Assert.Equal(200, (await owner.Put($"/products/batches/{batchId}", new { expiryDate = corrected, notes = "خطأ إدخال في الفاتورة" })).Status);
        Assert.Equal(corrected, DateTime.Parse(Assert.Single(await BatchesAsync(owner, item))!["expiryDate"].S()).Date);
        var audit = (await owner.Get($"/auditlogs?entityName=ItemBatch&entityId={batchId}")).Data!["items"]!.AsArray();
        Assert.Contains(audit, a => a!["action"].S() == "BATCH_CORRECTED");
    }
}
