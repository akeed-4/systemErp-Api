using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تتبّع التشغيلات والصلاحية: الوارد برقم تشغيلة وتاريخ انتهاء، والصادر بالأقرب انتهاءً (FEFO) مع منع بيع المنتهي،
/// والمرتجع لتشغيلات أصله، وعكس الفاتورة عند تعديلها لتشغيلاتها، والتحويل بأرقامها، وتفعيل التتبّع على رصيد قائم، والجرد بالتشغيلة.
/// </summary>
[Collection("api")]
public class ItemBatchTests : TestBase
{
    public ItemBatchTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static async Task<Guid> TrackedProductAsync(Client api, string sku = "MED", bool expiry = true)
    {
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        var p = await api.Post("/products", new
        {
            sku, nameAr = "دواء " + sku, category = "C1", unit = "PCS", sellingPrice = 100, vatRate = 15,
            trackBatches = true, trackExpiry = expiry, expiryAlertDays = 30,
        });
        Assert.Equal(201, p.Status);
        return p.Data!["id"].G();
    }

    private static object PurchaseLine(Guid item, decimal qty, string? batch, DateTime? expiry, decimal price = 40)
        => new { itemId = item, quantity = qty, unitPrice = price, vatRate = 15, batchNumber = batch, expiryDate = expiry };

    private static Task<Res> PurchaseAsync(Client api, Guid supplier, params object[] lines)
        => api.Post("/purchase-invoices", new { kind = "purchase", invoiceType = "simplified", paymentMethod = "credit", partyId = supplier, items = lines });

    private static Task<Res> SaleAsync(Client api, Guid item, decimal qty, Guid? batch = null)
        => api.Post("/sales-invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            items = new[] { new { itemId = item, quantity = qty, unitPrice = 100, vatRate = 15, itemBatchId = batch } },
        });

    private static async Task<Dictionary<string, decimal>> BatchesAsync(Client api, Guid item, bool includeEmpty = false)
        => (await api.Get($"/products/{item}/batches?includeEmpty={includeEmpty.ToString().ToLowerInvariant()}")).Data!.AsArray()
            .ToDictionary(b => b!["batchNumber"].S(), b => b!["quantity"].D());

    private static async Task<Guid> BatchIdAsync(Client api, Guid item, string number)
        => (await api.Get($"/products/{item}/batches?includeEmpty=true")).Data!.AsArray().First(b => b!["batchNumber"].S() == number)!["id"].G();

    private static void Ok201(Res r) => Assert.True(r.Status == 201, r.Body?.ToJsonString());

    private static JsonNode FirstLine(Res invoice) => invoice.Data!["items"]!.AsArray()[0]!;

    [Fact]
    public async Task Purchase_requires_batch_and_expiry_and_sale_takes_earliest_expiry_first_across_batches()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await TrackedProductAsync(api);

        // بلا رقم تشغيلة، وبلا صلاحية لصنف يتطلبها: مرفوضان بلا أثر
        Assert.Equal(400, (await PurchaseAsync(api, supplier, PurchaseLine(item, 5, null, Today.AddDays(90)))).Status);
        Assert.Equal(400, (await PurchaseAsync(api, supplier, PurchaseLine(item, 5, "L-NOEXP", null))).Status);
        Assert.Empty(await BatchesAsync(api, item, includeEmpty: true));

        Ok201(await PurchaseAsync(api, supplier,
            PurchaseLine(item, 5, "L-LATE", Today.AddDays(200)),
            PurchaseLine(item, 4, "L-EARLY", Today.AddDays(40))));
        // الرقم نفسه بتاريخ انتهاء مختلف: تشغيلة واحدة لا تحمل تاريخين
        Assert.Equal(409, (await PurchaseAsync(api, supplier, PurchaseLine(item, 1, "L-EARLY", Today.AddDays(41)))).Status);

        // البيع: الأقرب انتهاءً أولاً، ويُوزَّع السطر على تشغيلتين ويُكتب التوزيع عليه
        var sale = await SaleAsync(api, item, 6);
        Assert.Equal(201, sale.Status);
        var line = FirstLine(sale);
        Assert.Contains("L-EARLY", line["batchNumber"].S());
        Assert.Contains("L-LATE", line["batchNumber"].S());
        Assert.Equal(Today.AddDays(40), DateTime.Parse(line["expiryDate"].S()).Date);

        var left = await BatchesAsync(api, item);
        Assert.False(left.ContainsKey("L-EARLY"));
        Assert.Equal(3, left["L-LATE"]);
        Assert.Equal(3, (await api.Get($"/products/{item}")).Data!["currentStock"].D());
    }

    [Fact]
    public async Task Expired_batch_is_never_sold_and_automatic_selection_skips_it()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await TrackedProductAsync(api);
        // تشغيلة منتهية بالأمس (وارد قديم) وأخرى صالحة
        Ok201(await PurchaseAsync(api, supplier,
            PurchaseLine(item, 5, "EXP", Today.AddDays(-1)),
            PurchaseLine(item, 2, "OK", Today.AddDays(100))));
        var expired = await BatchIdAsync(api, item, "EXP");

        // اختيار المنتهية صراحةً مرفوض
        var chosen = await SaleAsync(api, item, 1, expired);
        Assert.Equal(409, chosen.Status);
        Assert.Contains("منتهية", chosen.Body!["message"].S());

        // التلقائي يتخطاها: الصالح 2 فقط، فطلب 3 مرفوض برسالة توضّح المنتهي
        var tooMuch = await SaleAsync(api, item, 3);
        Assert.Equal(409, tooMuch.Status);
        Assert.Contains("منتهي", tooMuch.Body!["message"].S());

        var sale = await SaleAsync(api, item, 2);
        Assert.Equal(201, sale.Status);
        Assert.StartsWith("OK", FirstLine(sale)["batchNumber"].S());
        Assert.Equal(5, (await BatchesAsync(api, item))["EXP"]);

        // تقرير الصلاحية: المنتهية بحالتها وقيمتها
        var report = (await api.Get("/reports/BatchExpiry?status=expired")).Data!.AsArray();
        var row = Assert.Single(report, r => r!["itemId"].G() == item)!;
        Assert.Equal("EXP", row["batchNumber"].S());
        Assert.Equal(5, row["quantity"].D());
        Assert.True(row["daysToExpiry"]!.GetValue<int>() < 0);
    }

    [Fact]
    public async Task Sales_return_goes_back_to_the_original_batches_and_editing_a_sale_restores_them()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await TrackedProductAsync(api);
        Ok201(await PurchaseAsync(api, supplier,
            PurchaseLine(item, 3, "A", Today.AddDays(30)),
            PurchaseLine(item, 10, "B", Today.AddDays(300))));

        var sale = await SaleAsync(api, item, 5); // A×3 ثم B×2
        Assert.Equal(201, sale.Status);
        var after = await BatchesAsync(api, item);
        Assert.False(after.ContainsKey("A"));
        Assert.Equal(8, after["B"]);

        // المرتجع على الأصل يعود لتشغيلاته بترتيبها (الأقرب انتهاءً أولاً)
        var ret = await api.Post("/sales-returns/from-invoice", new
        {
            originalInvoiceId = sale.Data!["id"].S(), returnReason = "مرتجع", lines = new[] { new { itemId = item, quantity = 4 } },
        });
        Assert.Equal(200, ret.Status);
        var returned = await BatchesAsync(api, item);
        Assert.Equal(3, returned["A"]);
        Assert.Equal(9, returned["B"]);

        // فاتورة بيع ثانية ثم تعديلها: العكس يعيد تشغيلاتها، وإعادة الترحيل تخصّص من جديد
        var second = await SaleAsync(api, item, 4); // A×3 ثم B×1
        Assert.Equal(201, second.Status);
        var id = second.Data!["id"].S();
        var edited = await api.Put($"/sales-invoices/{id}", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted",
            items = new[] { new { itemId = item, quantity = 1, unitPrice = 100, vatRate = 15 } },
        });
        Assert.Equal(200, edited.Status);
        var final = await BatchesAsync(api, item);
        Assert.Equal(2, final["A"]);
        Assert.Equal(9, final["B"]);
        Assert.Equal(11, (await api.Get($"/products/{item}")).Data!["currentStock"].D());
    }

    [Fact]
    public async Task Transfer_moves_the_same_batches_and_enabling_tracking_wraps_existing_stock_in_an_opening_batch()
    {
        var api = await NewTenantAsync();
        // صنف برصيد قائم قبل التتبّع: التفعيل يضع رصيده في تشغيلة افتتاحية
        var item = await SeedProductAsync(api, "OLD", stock: 6, cost: 40);
        var product = (await api.Get($"/products/{item}")).Data!.AsObject();
        product["trackBatches"] = true;
        Assert.Equal(200, (await api.Put($"/products/{item}", product)).Status);
        Assert.Equal(6, (await BatchesAsync(api, item))["OPENING"]);

        var (supplier, _) = await SeedSupplierAsync(api);
        Ok201(await PurchaseAsync(api, supplier, PurchaseLine(item, 4, "NEW", Today.AddDays(60))));

        // الإيقاف ممنوع والتشغيلات تحمل رصيداً
        product = (await api.Get($"/products/{item}")).Data!.AsObject();
        product["trackBatches"] = false;
        Assert.Equal(409, (await api.Put($"/products/{item}", product)).Status);

        var main = (await api.Get("/warehouses")).Data!["items"]!.AsArray().First(w => w!["isDefault"]!.GetValue<bool>())!["id"].G();
        var branch = (await api.Post("/warehouses", new { code = "BR", nameAr = "فرع", nameEn = "BR", location = "جدة", status = "active" })).Data!["id"].G();

        // بلا اختيار: الأقرب انتهاءً (NEW المؤرخة قبل الافتتاحية بلا صلاحية)، وتنتقل بأرقامها
        var transfer = await api.Post("/stocktransfers", new { fromWarehouseId = main, toWarehouseId = branch, items = new[] { new { itemId = item, quantity = 5 } } });
        Assert.Equal(200, transfer.Status);
        Assert.Contains("NEW", transfer.Data!["items"]![0]!["batchNumber"].S());

        var atBranch = (await api.Get($"/products/{item}/batches?warehouseId={branch}")).Data!.AsArray().ToDictionary(b => b!["batchNumber"].S(), b => b!["quantity"].D());
        Assert.Equal(4, atBranch["NEW"]);
        Assert.Equal(1, atBranch["OPENING"]);
        var atMain = (await api.Get($"/products/{item}/batches?warehouseId={main}")).Data!.AsArray().ToDictionary(b => b!["batchNumber"].S(), b => b!["quantity"].D());
        Assert.Equal(5, atMain["OPENING"]);
        Assert.False(atMain.ContainsKey("NEW"));
    }

    [Fact]
    public async Task Count_snapshot_lists_each_batch_and_approval_adjusts_that_batch_only()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await TrackedProductAsync(api, "CNT");
        Ok201(await PurchaseAsync(api, supplier,
            PurchaseLine(item, 5, "C1", Today.AddDays(20)),
            PurchaseLine(item, 5, "C2", Today.AddDays(90))));

        var snapshot = (await api.Post("/inventorycounts/snapshot", new { scope = "items" })).Data!.AsArray()
            .Where(l => l!["itemId"].G() == item).ToList();
        Assert.Equal(new[] { "C1", "C2" }, snapshot.Select(l => l!["batchNumber"].S()));

        var c1 = await BatchIdAsync(api, item, "C1");
        var c2 = await BatchIdAsync(api, item, "C2");
        var count = await api.Post("/inventorycounts", new
        {
            scope = "items", countType = "full", countDate = DateTime.UtcNow,
            lines = new object[]
            {
                new { itemId = item, itemBatchId = c1, countedQuantity = 3m }, // عجز 2 في C1
                new { itemId = item, itemBatchId = c2, countedQuantity = 5m },
                new { itemId = item, batchNumber = "C3-FOUND", expiryDate = Today.AddDays(120), countedQuantity = 1m }, // تشغيلة معثور عليها
            },
        });
        Assert.Equal(201, count.Status);
        var approval = await api.Post($"/inventorycounts/{count.Data!["id"].S()}/submit");
        Assert.Equal(200, (await api.Post($"/inventorycountapprovals/{approval.Data!["id"].S()}/approve", new { })).Status);

        var batches = await BatchesAsync(api, item);
        Assert.Equal(3, batches["C1"]);
        Assert.Equal(5, batches["C2"]);
        Assert.Equal(1, batches["C3-FOUND"]);
        Assert.Equal(9, (await api.Get($"/products/{item}")).Data!["currentStock"].D());
    }
}
