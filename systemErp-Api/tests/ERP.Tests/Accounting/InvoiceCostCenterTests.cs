using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// مركز التكلفة على كل أنواع الفواتير: فواتير المعارض (شراء/بيع)، الفواتير الناتجة عن عقود البيع وأوامر الشراء والعقود التجارية،
/// تعديل المسودة والمرحّلة، حذف المرحّلة (عكس قيدها بمراكزه)، معاينة القيد، وصلاحية تعيين المركز. المركز إضافة على أسطر القيد
/// فلا يغيّر مبالغها ولا توازنها.
/// </summary>
[Collection("api")]
public class InvoiceCostCenterTests : TestBase
{
    public InvoiceCostCenterTests(ErpFactory f) : base(f) { }

    private const string CostCenterScreen = "invoice-cost-center";

    private static async Task<Guid> CenterAsync(Client api, string code)
    {
        var created = await api.Post("/costcenters", new { code, nameAr = "مركز " + code, nameEn = code, isActive = true });
        Assert.Equal(201, created.Status);
        return created.Data!["id"].G();
    }

    private static Guid? CenterOf(JsonNode? line) => line?["costCenterId"] is { } c ? Guid.Parse(c.GetValue<string>()) : null;

    private static async Task<JsonArray> EntryLinesAsync(Client api, string entryId) => (await api.Get($"/journalentries/{entryId}")).Data!["lines"]!.AsArray();

    /// <summary>كل أسطر القيد على الحسابات التي تقبل المراكز تحمل المركز، ومجموع المدين = الدائن.</summary>
    private static void AssertTagged(JsonArray lines, Guid center, params string[] accounts)
    {
        foreach (var account in accounts)
            Assert.All(lines.Where(l => l!["accountCode"].S() == account), l => Assert.Equal(center, CenterOf(l)));
        Assert.Equal(lines.Sum(l => l!["debit"].D()), lines.Sum(l => l!["credit"].D()));
    }

    private static object CarLine(string vin, decimal price) => new
    {
        vin, brandNameAr = "تويوتا", modelNameAr = "كامري", trimNameAr = "GLE", year = 2025,
        colorExterior = "أبيض", colorInterior = "بيج", vatMode = "standard_15", unitPrice = price, discount = 0m,
    };

    private static object Service(string name, decimal price) => new { itemName = name, quantity = 1, unitPrice = price, vatRate = 15 };

    private async Task<(Client Rep, string Email)> SalesRepAsync(Client owner)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        return (await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!"), email);
    }

    private static async Task GrantCostCenterAsync(Client owner, string role)
    {
        var perms = (await owner.Get($"/permissions?roleId={role}")).Data!.AsArray();
        var granted = perms.Select(x => new
        {
            screenId = x!["screenId"].S(), screenNameAr = x["screenNameAr"].S(), screenNameEn = x["screenNameEn"].S(),
            canView = x["screenId"].S() == CostCenterScreen || x["canView"]!.GetValue<bool>(), canCreate = x["canCreate"]!.GetValue<bool>(),
            canEdit = x["screenId"].S() == CostCenterScreen || x["canEdit"]!.GetValue<bool>(),
            canDelete = x["canDelete"]!.GetValue<bool>(), canApprove = x["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = role, permissions = granted })).Status);
    }

    // ---------- فواتير المعارض ----------
    [Fact]
    public async Task Showroom_purchase_and_sales_invoices_post_every_line_to_the_chosen_center()
    {
        var api = await NewTenantAsync();
        var buying = await CenterAsync(api, "BUY");
        var selling = await CenterAsync(api, "SELL");
        var (supplier, _) = await SeedSupplierAsync(api);

        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "posted", costCenterId = buying,
            vehicleLines = new[] { CarLine(Client.NewVin('3'), 1000m) },
        });
        Assert.Equal(201, purchase.Status);
        Assert.Equal(buying, purchase.Data!["costCenterId"].G());
        Assert.All(purchase.Data["items"]!.AsArray(), i => Assert.Equal(buying, CenterOf(i)));
        var purchaseLines = await EntryLinesAsync(api, purchase.Data["journalEntryId"].S());
        Assert.Contains(purchaseLines, l => CenterOf(l) == buying);
        Assert.Equal(1150, purchaseLines.Sum(l => l!["debit"].D())); // المبالغ كما هي: المركز لا يغيّر القيد

        var vehicle = (await api.Get("/vehicles")).Data!["items"]!.AsArray()[0]!;
        var sale = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", costCenterId = selling,
            vehicleLines = new[] { new { vehicleId = vehicle["id"].S(), unitPrice = 1500m, discount = 0m, vatMode = "standard_15" } },
        });
        Assert.Equal(201, sale.Status);
        Assert.Equal(selling, sale.Data!["costCenterId"].G());
        var saleLines = await EntryLinesAsync(api, sale.Data["journalEntryId"].S());
        // الإيراد وتكلفة السيارة المباعة على مركز البيع
        Assert.True(saleLines.Where(l => CenterOf(l) == selling).Sum(l => l!["credit"].D()) >= 1500);
        Assert.Equal(saleLines.Sum(l => l!["debit"].D()), saleLines.Sum(l => l!["credit"].D()));

        // القيد الفعلي للفاتورة يعرض المركز برمزه
        var journal = (await api.Get(await api.InvoicePath(sale.Data["id"].S(), "/journal"))).Data!["lines"]!.AsArray();
        Assert.Contains(journal, l => l!["costCenterCode"].S() == "SELL");

        // تقرير المركز يرى إيراد البيع
        var report = (await api.Get($"/reports/JournalLedger?costCenterId={selling}")).Data!.AsArray();
        Assert.True(report.Where(r => r!["credit"].D() > 0).Sum(r => r!["credit"].D()) >= 1500);
    }

    [Fact]
    public async Task Journal_preview_shows_the_center_without_posting_anything()
    {
        var api = await NewTenantAsync();
        var center = await CenterAsync(api, "PRV");
        var (supplier, _) = await SeedSupplierAsync(api);

        var preview = await api.PreviewInvoiceJournal(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, costCenterId = center,
            vehicleLines = new[] { CarLine(Client.NewVin('4'), 2000m) },
        });
        Assert.Equal(200, preview.Status);
        Assert.True(preview.Data!["isPreview"]!.GetValue<bool>());
        Assert.Contains(preview.Data["lines"]!.AsArray(), l => CenterOf(l) == center && l!["costCenterCode"].S() == "PRV");
        Assert.Empty((await api.Get("/vehicles")).Data!["items"]!.AsArray());
    }

    // ---------- التعديل قبل الترحيل وبعده، والحذف ----------
    [Fact]
    public async Task Editing_a_draft_or_posted_invoice_moves_its_entry_to_the_new_center_and_deleting_reverses_it_in_place()
    {
        var api = await NewTenantAsync();
        var first = await CenterAsync(api, "C1");
        var second = await CenterAsync(api, "C2");

        var draft = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft", costCenterId = first, items = new[] { Service("خدمة", 100) } });
        Assert.Equal(201, draft.Status);
        var id = draft.Data!["id"].S();

        // تعديل المسودة بمركز آخر ثم ترحيلها: الأسطر والقيد على المركز الجديد وحده
        var edited = await api.Put(await api.InvoicePath(id), new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", costCenterId = second, items = new[] { Service("خدمة", 100) } });
        Assert.Equal(200, edited.Status);
        Assert.All(edited.Data!["items"]!.AsArray(), i => Assert.Equal(second, CenterOf(i)));
        var entryId = edited.Data["journalEntryId"].S();
        var lines = await EntryLinesAsync(api, entryId);
        Assert.Equal(100, lines.Where(l => CenterOf(l) == second).Sum(l => l!["credit"].D()));
        Assert.DoesNotContain(lines, l => CenterOf(l) == first);

        // تعديل المرحّلة: قيدها يُستبدل في مكانه بالمركز الأول
        var reposted = await api.Put(await api.InvoicePath(id), new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", costCenterId = first, items = new[] { Service("خدمة", 100) } });
        Assert.Equal(200, reposted.Status);
        Assert.Equal(entryId, reposted.Data!["journalEntryId"].S());
        lines = await EntryLinesAsync(api, entryId);
        Assert.Equal(100, lines.Where(l => CenterOf(l) == first).Sum(l => l!["credit"].D()));
        Assert.DoesNotContain(lines, l => CenterOf(l) == second);

        // الحذف يعكس القيد بمراكزه نفسها: صافي المركز صفر
        Assert.Equal(200, (await api.Delete(await api.InvoicePath(id))).Status);
        var rows = (await api.Get($"/reports/JournalLedger?costCenterId={first}")).Data!.AsArray();
        Assert.Equal(rows.Sum(r => r!["debit"].D()), rows.Sum(r => r!["credit"].D()));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- الفواتير الناتجة عن مستندات المصدر ----------
    [Fact]
    public async Task Car_sales_contract_and_procurement_order_carry_their_center_to_the_generated_invoice()
    {
        var api = await NewTenantAsync();
        var buying = await CenterAsync(api, "PRC");
        var selling = await CenterAsync(api, "CSC");
        var (supplier, _) = await SeedSupplierAsync(api, "مورد سيارات");

        var order = await api.Post("/carprocurementorders/QuickPurchase", new
        {
            purchaseCycle = "corporate", supplierId = supplier, paymentType = "credit", creditDays = 30, costCenterId = buying,
            brandName = "تويوتا", modelName = "كامري", trimName = "GLE", year = 2025, colorExterior = "أبيض", colorInterior = "بيج",
            vin = Client.NewVin('5'), purchasePrice = 80000, sellingPrice = 100000, warehouseLocation = "المعرض",
        });
        Assert.Equal(201, order.Status);
        Assert.Equal(buying, order.Data!["costCenterId"].G());
        var purchaseInvoice = (await api.Get(await api.InvoicePath(order.Data["purchaseInvoiceId"].S()))).Data!;
        Assert.Equal(buying, purchaseInvoice["costCenterId"].G());
        AssertTagged(await EntryLinesAsync(api, purchaseInvoice["journalEntryId"].S()), buying, purchaseInvoice["inventoryAccountCode"].S());

        var vehicle = (await api.Get("/vehicles")).Data!["items"]!.AsArray()[0]!["id"].S();
        var sale = await api.Post("/carsalescontracts/QuickSale", new
        {
            contract = new
            {
                cycleType = "individual", buyerName = "م", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000",
                vehicleId = vehicle, sellingPrice = 100000, vatMode = "standard_15", paymentMethod = "cash", condition = "new", costCenterId = selling,
            },
        });
        Assert.Equal(201, sale.Status);
        Assert.Equal(selling, sale.Data!["costCenterId"].G());
        var salesInvoice = (await api.Get(await api.InvoicePath(sale.Data["invoiceId"].S()))).Data!;
        Assert.Equal(selling, salesInvoice["costCenterId"].G());
        var lines = await EntryLinesAsync(api, salesInvoice["journalEntryId"].S());
        AssertTagged(lines, selling, salesInvoice["revenueAccountCode"].S(), salesInvoice["cogsAccountCode"].S());
        Assert.Equal(100000, lines.Where(l => l!["accountCode"].S() == salesInvoice["revenueAccountCode"].S()).Sum(l => l!["credit"].D()));
    }

    [Fact]
    public async Task Commercial_contract_milestone_invoice_posts_revenue_to_the_contract_center()
    {
        var api = await NewTenantAsync();
        var center = await CenterAsync(api, "CTR");
        var (customer, _) = await SeedCustomerAsync(api);
        var ct = await api.Post("/commercialcontracts", new
        {
            title = "عقد صيانة", contractType = "maintenance", partyName = "عميل", partyId = customer, contractValue = 1000, vatRate = 15, costCenterId = center,
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(12),
            milestones = new[] { new { title = "دفعة 1", percentage = 100 } }, clauses = Array.Empty<object>(),
        });
        var id = ct.Data!["id"].S(); var m1 = ct.Data["milestones"]![0]!["id"].S();
        Assert.Equal(200, (await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" })).Status);

        var bill = await api.Post($"/commercialcontracts/{id}/milestones/{m1}/bill");
        Assert.Equal(200, bill.Status);
        Assert.Equal(center, bill.Data!["costCenterId"].G());
        var lines = await EntryLinesAsync(api, bill.Data["journalEntryId"].S());
        Assert.Equal(1000, lines.Where(l => CenterOf(l) == center).Sum(l => l!["credit"].D()));
    }

    // ---------- مراكز يختارها المستخدم لحسابات القيد ----------
    [Fact]
    public async Task Centers_chosen_per_journal_account_tag_its_lines_on_preview_post_edit_and_return_without_changing_amounts()
    {
        var api = await NewTenantAsync();
        var revenue = await CenterAsync(api, "REV");
        var other = await CenterAsync(api, "OTH");
        object Sale(Guid center, string status = "posted") => new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status,
            items = new[] { Service("خدمة", 200) },
            journalCostCenters = new[] { new { accountCode = "411", costCenterId = center } },
        };

        // المعاينة تعرض المركز المختار على سطر الإيراد
        var preview = await api.PreviewInvoiceJournal(Sale(revenue));
        Assert.Equal(200, preview.Status);
        Assert.All(preview.Data!["lines"]!.AsArray().Where(l => l!["accountCode"].S() == "411"), l => Assert.Equal(revenue, CenterOf(l)));

        var sale = await api.PostInvoice(Sale(revenue));
        Assert.Equal(201, sale.Status);
        Assert.Equal(revenue, sale.Data!["journalCostCenters"]![0]!["costCenterId"].G());
        var entryId = sale.Data["journalEntryId"].S();
        var lines = await EntryLinesAsync(api, entryId);
        Assert.Equal(200, lines.Where(l => l!["accountCode"].S() == "411" && CenterOf(l) == revenue).Sum(l => l!["credit"].D()));
        Assert.Null(CenterOf(lines.First(l => l!["accountCode"].S() == "1111"))); // الحسابات غير المختارة بلا مركز
        Assert.Equal(230, lines.Sum(l => l!["debit"].D()));                        // المبالغ والضريبة كما هي

        // تعديل المرحّلة: القيد يُستبدل في مكانه على المركز الجديد
        var edited = await api.Put(await api.InvoicePath(sale.Data["id"].S()), Sale(other));
        Assert.Equal(200, edited.Status);
        lines = await EntryLinesAsync(api, entryId);
        Assert.Equal(200, lines.Where(l => l!["accountCode"].S() == "411" && CenterOf(l) == other).Sum(l => l!["credit"].D()));

        // المرتجع يعكس الإيراد على مركز أصله
        var ret = await api.PostReturn(new { originalInvoiceId = sale.Data["id"].S(), returnReason = "x" });
        Assert.Equal(200, ret.Status);
        var retLines = await EntryLinesAsync(api, ret.Data!["journalEntryId"].S());
        Assert.Contains(retLines, l => l!["accountCode"].S() is "411" or "412" && CenterOf(l) == other);
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);

        // اختيار مركز للقيد يحتاج صلاحية تعيين المراكز
        var (rep, _) = await SalesRepAsync(api);
        Assert.Equal(403, (await rep.PostInvoice(Sale(revenue))).Status);
    }

    // ---------- الصلاحيات ----------
    [Fact]
    public async Task Assigning_a_center_needs_the_cost_center_permission_but_keeping_the_saved_one_does_not()
    {
        var owner = await NewTenantAsync();
        var center = await CenterAsync(owner, "AUTH");
        var other = await CenterAsync(owner, "AUTH2");
        var (rep, repEmail) = await SalesRepAsync(owner); // المندوب لا يرث صلاحية دليل الحسابات

        object Sale(Guid? header, string status = "draft") => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status, costCenterId = header, items = new[] { Service("خدمة", 100) } };

        Assert.Equal(403, (await rep.PostInvoice(Sale(center))).Status);
        Assert.Equal(403, (await rep.PreviewInvoiceJournal(Sale(center))).Status);
        Assert.Equal(201, (await rep.PostInvoice(Sale(null))).Status); // بلا مركز: كما كان

        // مسودة بمركز أنشأها المالك: يعدّلها المندوب بمركزها نفسه، ولا يغيّره ولا يمسحه
        var draft = await owner.PostInvoice(Sale(center));
        var path = await owner.InvoicePath(draft.Data!["id"].S());
        Assert.Equal(200, (await rep.Put(path, Sale(center))).Status);
        Assert.Equal(403, (await rep.Put(path, Sale(other))).Status);
        Assert.Equal(403, (await rep.Put(path, Sale(null))).Status);

        // المرتجع يرث مراكز أصله دون صلاحية
        var posted = await owner.PostInvoice(Sale(center, "posted"));
        Assert.Equal(200, (await rep.PostReturn(new { originalInvoiceId = posted.Data!["id"].S(), returnReason = "x" })).Status);

        await GrantCostCenterAsync(owner, "sales_rep");
        rep = await owner.LoginAsAsync(NewHttp(), repEmail, "Passw0rd!");
        Assert.Equal(201, (await rep.PostInvoice(Sale(other))).Status);
    }

    [Fact]
    public async Task Car_contract_center_is_validated_and_needs_the_permission()
    {
        var owner = await NewTenantAsync();
        var center = await CenterAsync(owner, "CAR");
        var car = await owner.Post("/vehicles", new
        {
            chassisNumber = Client.NewVin('6'), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new", purchasePrice = 40000, sellingPrice = 50000,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض",
        });
        object Contract(Guid? cc) => new
        {
            cycleType = "individual", buyerName = "فرد", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000",
            vehicleId = car.Data!["id"].S(), sellingPrice = 50000, vatMode = "standard_15", paymentMethod = "cash", condition = "new", costCenterId = cc,
        };

        Assert.Equal(400, (await owner.Post("/carsalescontracts", Contract(Guid.NewGuid()))).Status); // مركز غير موجود
        var (rep, _) = await SalesRepAsync(owner);
        Assert.Equal(403, (await rep.Post("/carsalescontracts", Contract(center))).Status);
        var created = await owner.Post("/carsalescontracts", Contract(center));
        Assert.Equal(201, created.Status);
        Assert.Equal(center, created.Data!["costCenterId"].G());
    }
}
