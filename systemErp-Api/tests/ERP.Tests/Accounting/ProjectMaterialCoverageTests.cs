using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تغطية مواد المشروع (المرحلة 1): صافي الشراء بعد الرصيد وما في الطريق، بلا عدّ مزدوج عبر دورة الشراء القائمة
/// (طلب ← اعتماد ← فاتورة شراء مسودة ← ترحيل = استلام في المستودع ← إذن تسليم للموقع).
/// الأسمنت مادة بالكيس (50 كجم) مربوطة بصنف بالكيلو: قواعد 50 × 7 أكياس + بلاطات 100 × 2 × 1.05 = 560 كيساً = 28,000 كجم.
/// الرمل بلا صنف مخزني: لا يُحكم عليه (لا اكتمال ولا نقص).
/// </summary>
[Collection("api")]
public class ProjectMaterialCoverageTests : TestBase
{
    public ProjectMaterialCoverageTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;
    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Project(Client Api, string Id, Guid Cement, Guid Supplier, Guid BaseLine);

    private async Task<Project> ProjectAsync(decimal stockKg)
    {
        var api = await NewConstructionTenantAsync();
        Ok(await api.Post("/unitsofmeasure", new { code = "KG", nameAr = "كجم", unitType = "weight", isBaseUnit = true, conversionFactor = 1 }), "KG");
        Ok(await api.Post("/unitsofmeasure", new { code = "BAG", nameAr = "كيس 50 كجم", unitType = "weight", isBaseUnit = false, baseUnitCode = "KG", conversionFactor = 50 }), "BAG");
        Ok(await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "عدد", unitType = "count", isBaseUnit = true, conversionFactor = 1 }), "PCS");
        Ok(await api.Post("/unitsofmeasure", new { code = "M3", nameAr = "م3", unitType = "volume", isBaseUnit = true, conversionFactor = 1 }), "M3");
        await api.Post("/productcategories", new { code = "C1", nameAr = "مواد" });
        async Task<Guid> Product(string sku, string unit)
        {
            var p = await api.Post("/products", new { sku, nameAr = sku, category = "C1", unit, sellingPrice = 1, vatRate = 15 });
            Ok(p, sku);
            return p.Data!["id"].G();
        }
        var cementProduct = await Product("CEM-KG", "KG");
        if (stockKg > 0)
            Ok(await api.Post("/stockmovements/adjust", new { itemId = cementProduct, type = "adjustment_in", quantity = stockKg, unitCost = 0.4m, referenceNumber = "OPEN", counterAccountCode = "33" }), "stock");

        async Task<Guid> R(object body) { var r = await api.Post("/construction-resources", body); Ok(r, "resource"); return r.Data!["id"].G(); }
        var cement = await R(new { nameAr = "أسمنت", resourceType = "material", unit = "BAG", costSource = "product", productId = cementProduct });
        var sand = await R(new { nameAr = "رمل", resourceType = "material", unit = "M3", costSource = "manual", standardCost = 60 });
        // ربط مادة بصنف وحدته لا تتحول مرفوض من الأصل: تنبيه unit_mismatch احتياط لتغيّر الوحدات بعد الربط
        Assert.Equal(400, (await api.Post("/construction-resources", new { nameAr = "حديد", resourceType = "material", unit = "M3", costSource = "manual", standardCost = 3, productId = cementProduct })).Status);
        object Need(Guid r, decimal f, decimal w = 0) => new { resourceId = r, quantityMode = "per_unit", consumptionFactor = f, wastePercent = w };
        var baseItem = await api.Post("/work-items", new { nameAr = "قاعدة", unit = "PCS", pricingMode = "manual", sellPrice = 600, sectionName = "قواعد",
            components = new[] { Need(cement, 7), Need(sand, 0.5m, 10) } });
        var slab = await api.Post("/work-items", new { nameAr = "بلاطة", unit = "PCS", pricingMode = "manual", sellPrice = 300, sectionName = "بلاطات",
            components = new[] { Need(cement, 2, 5) } });
        Ok(baseItem, "base"); Ok(slab, "slab");

        var (customer, _) = await SeedCustomerAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);
        var s = await api.Post("/commercialcontracts", new
        {
            title = "تغطية", projectName = "تغطية", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = customer,
            startDate = Today, endDate = Today.AddDays(59), vatRate = 15, isFeasibilityStudy = true, milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
            items = new object[]
            {
                new { description = "قواعد", unit = "عدد", quantity = 50, unitPrice = 600, vatRate = 15, lineKind = "measured", workItemId = baseItem.Data!["id"].G(), sortOrder = 1 },
                new { description = "بلاطات", unit = "عدد", quantity = 100, unitPrice = 300, vatRate = 15, lineKind = "measured", workItemId = slab.Data!["id"].G(), sortOrder = 2 },
            },
        });
        Ok(s, "study");
        var id = s.Data!["id"].S();
        var baseLine = s.Data["items"]!.AsArray().Single(i => i!["description"].S() == "قواعد")!["id"].G();
        Ok(await api.Post($"/commercialcontracts/{id}/cost-analysis/apply", new { }), "price");
        Ok(await api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { }), "plan");
        Ok(await api.Post($"/commercialcontracts/{id}/execution-plan/submit", new { }), "submit");
        Ok(await api.Post($"/commercialcontracts/{id}/execution-plan/approve", new { }), "approve");
        Ok(await api.Post($"/feasibility-studies/{id}/create-contract", new { }), "contract");
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        return new Project(api, id, cementProduct, supplier, baseLine);
    }

    private static async Task<JsonNode> TrackingAsync(Project p)
    {
        var r = await p.Api.Get($"/commercialcontracts/{p.Id}/material-tracking");
        Ok(r, "tracking");
        return r.Data!;
    }

    private static async Task<JsonNode> CementAsync(Project p)
        => (await TrackingAsync(p))["lines"]!.AsArray().Single(l => l!["productId"].G() == p.Cement)!;

    private static string[] Codes(JsonNode line) => line["alerts"]!.AsArray().Select(a => a!["code"].S()).ToArray();
    private static decimal AlertQty(JsonNode line, string code) => line["alerts"]!.AsArray().Single(a => a!["code"].S() == code)!["quantity"].D();

    /// <summary>طلب شراء للمشروع بالكيلو (مسودة)، واعتماده وتحويله اختيارياً؛ يعيد الطلب ومعرّف الفاتورة إن حُوِّل.</summary>
    private static async Task<(string Requisition, string? Invoice)> RequestAsync(Project p, decimal kg, bool approve = false, bool convert = false)
    {
        var r = await p.Api.Post("/materialrequisitions", new
        {
            requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(5), department = "المشاريع", requestedBy = "م. خالد", priority = "high",
            supplierId = p.Supplier, supplierName = "مورد", status = "draft", contractId = p.Id,
            items = new[] { new { itemId = p.Cement, itemName = "أسمنت", sku = "CEM-KG", unit = "KG", requestedQuantity = kg, estimatedCost = 0.4m } },
        });
        Ok(r, $"request {kg}");
        var id = r.Data!["id"].S();
        if (approve || convert) Ok(await p.Api.Post($"/materialrequisitions/{id}/approve"), "approve request");
        if (!convert) return (id, null);
        var inv = await p.Api.Post($"/materialrequisitions/{id}/ConvertToInvoice");
        Ok(inv, "convert");
        return (id, inv.Data!["id"].S());
    }

    [Fact]
    public async Task Each_quantity_is_counted_once_through_the_purchase_cycle()
    {
        var p = await ProjectAsync(stockKg: 10_000);

        // البداية: مادة مشتركة بين بندين ومحوّلة من الكيس للكيلو؛ الرصيد يغطي جزءاً
        var c = await CementAsync(p);
        Assert.Equal(28_000m, c["requiredQuantity"].D());
        Assert.Equal(10_000m, c["warehouseStock"].D());
        Assert.Equal("partial", c["coverageStatus"].S());
        Assert.Equal(18_000m, c["netToPurchase"].D());
        Assert.Equal(18_000m, c["suggestedRequestQuantity"].D());
        Assert.Equal(18_000m, AlertQty(c, "partially_available"));
        Assert.Equal(18_000m, AlertQty(c, "purchase_needed"));

        // الرمل بلا صنف مخزني: لا يُحكم عليه ولا يُعدّ نقصاً
        var unverifiable = Assert.Single((await TrackingAsync(p))["unverifiable"]!.AsArray())!;
        Assert.Equal("not_stocked", unverifiable["alert"]!["code"].S());
        Assert.Equal(27.5m, unverifiable["quantity"].D());   // 50 × 0.5 × 1.1 م3
        Assert.Equal("M3", unverifiable["unit"].S());

        // طلب معلّق (لم يُعتمد): في الطريق، ولا يُعدّ مستلماً
        var (r1, _) = await RequestAsync(p, 5_000);
        c = await CementAsync(p);
        Assert.Equal(5_000m, c["pendingRequestQuantity"].D());
        Assert.Equal(0m, c["approvedRequestQuantity"].D());
        Assert.Equal(13_000m, c["netToPurchase"].D());
        Assert.Contains("pending_requests", Codes(c));

        // اعتماده: يبقى معلّقاً (لم يُحوَّل)، ولا يتغير الصافي
        Ok(await p.Api.Post($"/materialrequisitions/{r1}/approve"), "approve r1");
        c = await CementAsync(p);
        Assert.Equal(5_000m, c["approvedRequestQuantity"].D());
        Assert.Equal(13_000m, c["netToPurchase"].D());

        // طلب ثانٍ حُوِّل لفاتورة شراء مسودة: مطلوب غير مستلم، وليس معلّقاً أيضاً — مثال 28,000 − 10,000 − 5,000 − 2,000 = 11,000
        var (_, inv2) = await RequestAsync(p, 2_000, convert: true);
        c = await CementAsync(p);
        Assert.Equal(5_000m, c["pendingRequestQuantity"].D());
        Assert.Equal(2_000m, c["orderedQuantity"].D());
        Assert.Equal(7_000m, c["requestedQuantity"].D());
        Assert.Equal(11_000m, c["netToPurchase"].D());
        Assert.Equal(2_000m, AlertQty(c, "ordered_not_received"));

        // الاستلام (ترحيل الفاتورة): يدخل الرصيد ويخرج من المطلوب — لا يُطرح مرتين
        Ok(await p.Api.Post($"/purchase-invoices/{inv2}/post"), "post inv2");
        c = await CementAsync(p);
        Assert.Equal(0m, c["orderedQuantity"].D());
        Assert.Equal(2_000m, c["receivedQuantity"].D());
        Assert.Equal(12_000m, c["warehouseStock"].D());
        Assert.Equal(11_000m, c["netToPurchase"].D());

        // تحويل الطلب المعتمد: ينتقل من المعلّق إلى المطلوب (تداخل الطلب والفاتورة لا يتكرر)
        var conv = await p.Api.Post($"/materialrequisitions/{r1}/ConvertToInvoice");
        Ok(conv, "convert r1");
        c = await CementAsync(p);
        Assert.Equal(0m, c["pendingRequestQuantity"].D());
        Assert.Equal(5_000m, c["orderedQuantity"].D());
        Assert.Equal(11_000m, c["netToPurchase"].D());

        // استلام جزئي: الفاتورة عُدّلت لـ 3,000 ثم رُحّلت؛ الـ 2,000 غير المستلمة تعود للصافي
        var invoiceId = conv.Data!["id"].S();
        var draft = (await p.Api.Get($"/purchase-invoices/{invoiceId}")).Data!.AsObject();
        draft["items"]![0]!["quantity"] = 3_000;
        draft["status"] = "posted";
        Ok(await p.Api.Put($"/purchase-invoices/{invoiceId}", draft), "partial receipt");
        c = await CementAsync(p);
        Assert.Equal(0m, c["orderedQuantity"].D());
        Assert.Equal(5_000m, c["receivedQuantity"].D());
        Assert.Equal(15_000m, c["warehouseStock"].D());
        Assert.Equal(13_000m, c["netToPurchase"].D());   // 28,000 − 15,000

        // التسليم للموقع: يخرج من الرصيد ومن المتبقي معاً
        var issue = await p.Api.Post($"/commercialcontracts/{p.Id}/material-issues", new
        {
            receiverType = "site_engineer", receiverName = "م. خالد", post = true,
            lines = new[] { new { boqItemId = p.BaseLine, productId = p.Cement, quantity = 15_000m } },
        });
        Ok(issue, "deliver");
        c = await CementAsync(p);
        Assert.Equal(15_000m, c["deliveredQuantity"].D());
        Assert.Equal(0m, c["warehouseStock"].D());
        Assert.Equal(13_000m, c["remainingToDeliver"].D());
        Assert.Equal("none", c["coverageStatus"].S());
        Assert.Equal(13_000m, c["netToPurchase"].D());
        var noStock = c["alerts"]!.AsArray().Single(a => a!["code"].S() == "no_stock")!;
        Assert.Equal("critical", noStock["severity"].S());
        Assert.Equal(13_000m, c["suggestedRequestQuantity"].D()); // ≤ المتبقي للطلب 21,000
    }

    [Fact]
    public async Task Full_stock_suggests_nothing_and_extra_requests_are_flagged_as_excess()
    {
        var p = await ProjectAsync(stockKg: 30_000);
        var c = await CementAsync(p);
        Assert.Equal("full", c["coverageStatus"].S());
        Assert.Equal(0m, c["netToPurchase"].D());
        Assert.Equal(0m, c["suggestedRequestQuantity"].D());
        Assert.Equal(28_000m, AlertQty(c, "fully_available"));
        Assert.DoesNotContain("purchase_needed", Codes(c));

        // أمر شراء عام غير مرتبط بالمشروع لا يُنسب له
        Ok(await p.Api.Post("/commercialorders", new
        {
            type = "purchase_order", partyId = p.Supplier, partyType = "supplier", partyName = "مورد", orderDate = DateTime.UtcNow, expectedDeliveryDate = DateTime.UtcNow.AddDays(3),
            items = new[] { new { itemId = p.Cement, itemName = "أسمنت", quantity = 9_999, unitPrice = 0.4m, vatRate = 15 } },
        }), "generic purchase order");
        c = await CementAsync(p);
        Assert.Equal(0m, c["orderedQuantity"].D());

        // طلب رغم كفاية الرصيد: زائد عن الحاجة
        await RequestAsync(p, 3_000);
        c = await CementAsync(p);
        Assert.Equal(3_000m, c["excessPurchaseQuantity"].D());
        Assert.Equal(3_000m, AlertQty(c, "excess_purchase"));
        Assert.Equal(0m, c["netToPurchase"].D());
    }

    [Fact]
    public async Task No_stock_means_not_covered_and_the_whole_remainder_is_suggested()
    {
        var p = await ProjectAsync(stockKg: 0);
        var c = await CementAsync(p);
        Assert.Equal("none", c["coverageStatus"].S());
        Assert.Equal(28_000m, c["netToPurchase"].D());
        Assert.Equal(28_000m, c["suggestedRequestQuantity"].D());
        Assert.Equal("critical", c["alerts"]!.AsArray().Single(a => a!["code"].S() == "no_stock")!["severity"].S());

        // ما في الطريق يغطي الناقص كله: لا شراء إضافي، والتنبيه تحذير لا حرج
        await RequestAsync(p, 28_000);
        c = await CementAsync(p);
        Assert.Equal(0m, c["netToPurchase"].D());
        Assert.Equal("warning", c["alerts"]!.AsArray().Single(a => a!["code"].S() == "no_stock")!["severity"].S());
        Assert.Equal(0m, c["excessPurchaseQuantity"].D());
    }
}
