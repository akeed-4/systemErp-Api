using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>المرحلة الرابعة: القيود التلقائية لمصاريف السيارات من حسابات الإعدادات، محفظة تم، والإعدادات الضريبية.</summary>
[Collection("api")]
public class ChargePostingAndTaxTests : TestBase
{
    public ChargePostingAndTaxTests(ErpFactory f) : base(f) { }

    private static async Task<Dictionary<string, string>> TypeIdsAsync(Client api)
        => (await api.Get("/expensetypes?pageSize=200")).Data!["items"]!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].S());

    /// <summary>رصيد الحساب بطبيعته؛ صفر لحساب لم يُنشأ بعد.</summary>
    private static async Task<decimal> BalanceAsync(Client api, string code)
    {
        var r = await api.Get($"/accounts/ByCode/{code}");
        return r.Status == 200 ? r.Data!["balance"].D() : 0;
    }

    private static async Task AssertBalancedAsync(Client api)
    {
        decimal d = 0, c = 0;
        foreach (var row in (await api.Get("/reports/TrialBalance")).Data!.AsArray()) { d += row!["periodDebit"].D(); c += row["periodCredit"].D(); }
        Assert.Equal(d, c);
    }

    private static async Task<(string Id, string Vin)> SeedCarAsync(Client api, decimal purchase = 40000, decimal selling = 50000, string? vin = null, decimal additional = 0,
        string vatMode = "standard_15", string condition = "new")
    {
        vin ??= Client.NewVin('9');
        var car = await api.Post("/vehicles", new
        {
            chassisNumber = vin, brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition, mileageKm = condition == "used" ? 40000 : 0,
            purchasePrice = purchase, additionalCosts = additional, sellingPrice = selling,
            vatMode, fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض",
        });
        Assert.Equal(201, car.Status);
        return (car.Data!["id"].S(), vin);
    }

    private static Task<Res> ChargeAsync(Client api, string vehicleId, string typeId, decimal amount, string status = "actual", decimal vatAmount = 0,
        Guid? supplierId = null, string? paymentAccountCode = null)
        => api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vehicleId, expenseTypeId = typeId, amount, status, vatAmount, supplierId, paymentAccountCode });

    [Fact]
    public async Task Actual_charges_post_by_treatment_and_estimates_post_nothing()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (vid, _) = await SeedCarAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api, "شركة الشحن");

        // تقديري: لا قيد
        var estimated = await ChargeAsync(api, vid, types["FREIGHT"], 1000, "estimated", supplierId: supplier);
        Assert.Null(estimated.Data!["journalEntryId"]);
        Assert.Equal(0, await BalanceAsync(api, "1142"));

        // إضافة للتكلفة من مورد: من مخزون السيارات وضريبة المدخلات إلى المورد
        var freight = await ChargeAsync(api, vid, types["FREIGHT"], 1000, vatAmount: 150, supplierId: supplier);
        Assert.NotNull(freight.Data!["journalEntryId"]);
        Assert.Equal(1000, await BalanceAsync(api, "1142"));
        Assert.Equal(150, await BalanceAsync(api, "1131"));
        Assert.Equal(1150, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());

        // مصروف على المعرض مدفوع نقداً: من حساب المصروف إلى الصندوق
        Assert.Equal(201, (await ChargeAsync(api, vid, types["SALES_COMMISSION"], 500, paymentAccountCode: "1111")).Status);
        Assert.Equal(500, await BalanceAsync(api, "529"));
        Assert.Equal(-500, await BalanceAsync(api, "1111"));
        Assert.Equal(400, (await ChargeAsync(api, vid, types["SALES_COMMISSION"], 500, paymentAccountCode: "99999")).Status);

        // بلا مورد ولا حساب دفع: مستحقات
        var customs = await ChargeAsync(api, vid, types["CUSTOMS_DUTY"], 700);
        Assert.Equal(1700, await BalanceAsync(api, "1142"));
        Assert.Equal(700, await BalanceAsync(api, "212"));

        // ضريبة استيراد مستردة: ضريبة مدخلات لا مخزون
        Assert.Equal(201, (await ChargeAsync(api, vid, types["IMPORT_VAT"], 6000)).Status);
        Assert.Equal(6150, await BalanceAsync(api, "1131"));
        Assert.Equal(1700, await BalanceAsync(api, "1142"));
        Assert.Equal(6700, await BalanceAsync(api, "212"));

        // تعديل سطر مرحَّل: يُعكس قيده ويُرحَّل من جديد
        Assert.Equal(200, (await api.Put($"/additionalcharges/{customs.Data!["id"].S()}", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["CUSTOMS_DUTY"], amount = 900, status = "actual" })).Status);
        Assert.Equal(1900, await BalanceAsync(api, "1142"));
        Assert.Equal(6900, await BalanceAsync(api, "212"));
        Assert.Equal(41900, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());

        // تحقُّق التقديري يرحّله، والإلغاء والحذف يعكسان القيد
        Assert.Equal(200, (await api.Put($"/additionalcharges/{estimated.Data["id"].S()}", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["FREIGHT"], amount = 1000, status = "actual", supplierId = supplier })).Status);
        Assert.Equal(2900, await BalanceAsync(api, "1142"));
        Assert.Equal(200, (await api.Post($"/additionalcharges/{freight.Data["id"].S()}/cancel")).Status);
        Assert.Equal(1900, await BalanceAsync(api, "1142"));
        Assert.Equal(6000, await BalanceAsync(api, "1131"));
        Assert.Equal(200, (await api.Delete($"/additionalcharges/{estimated.Data["id"].S()}")).Status);
        Assert.Equal(900, await BalanceAsync(api, "1142"));
        Assert.Equal(0, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());
        Assert.Equal(40900, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Recoverable_vat_on_car_charges_reaches_the_vat_return()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (vid, _) = await SeedCarAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api, "الورشة");
        var from = DateTime.UtcNow.AddDays(-40).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        async Task<(decimal Vat, decimal Base)> ReturnAsync()
        {
            var r = await api.Get($"/reports/VatReturn?from={from}&to={to}");
            Assert.Equal(200, r.Status);
            return (r.Data!["inputVat"].D(), r.Data["standardRatedPurchases"].D());
        }
        Assert.Equal((0m, 0m), await ReturnAsync());

        // مصروف فعلي بضريبة مستردة، وضريبة استيراد، وتقديري لا أثر له
        var repair = await ChargeAsync(api, vid, types["REPAIR"], 1000, vatAmount: 150, supplierId: supplier);
        Assert.Equal(201, (await ChargeAsync(api, vid, types["IMPORT_VAT"], 6000)).Status);
        Assert.Equal(201, (await ChargeAsync(api, vid, types["POLISHING"], 400, "estimated", vatAmount: 60)).Status);
        Assert.Equal((6150m, 1000m), await ReturnAsync());

        // سند تكاليف إضافية ومصروف مدفوع مقدماً
        var voucher = await api.Post("/landedcostvouchers", new { supplierId = supplier, vehicleIds = new[] { vid }, lines = new[] { new { expenseTypeId = types["CUSTOMS_CLEARANCE"], amount = 400, vatAmount = 60 } } });
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{voucher.Data!["id"].S()}/approve")).Status);
        Assert.Equal(201, (await api.Post("/prepaidexpenses", new { expenseTypeId = types["TAMM_SUBSCRIPTION"], startDate = DateTime.UtcNow, months = 12, totalAmount = 1200, vatAmount = 180, paymentAccountCode = "1112" })).Status);
        Assert.Equal((6390m, 2600m), await ReturnAsync());
        Assert.Equal(6390, await BalanceAsync(api, "1131")); // الإقرار يطابق حساب ضريبة المدخلات

        // إلغاء المصروف يعكس قيده ويخرجه من الإقرار
        Assert.Equal(200, (await api.Post($"/additionalcharges/{repair.Data!["id"].S()}/cancel")).Status);
        Assert.Equal((6240m, 1600m), await ReturnAsync());
        Assert.Equal(6240, await BalanceAsync(api, "1131"));
    }

    [Fact]
    public async Task Charges_on_in_transit_vehicles_post_to_transit_and_move_to_inventory_on_receipt()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow, customsDutyFee = 600,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 2, unitPrice = 80000 } },
        });
        var id = order.Data!["id"].S();
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = stage });

        // شحن فعلي والسيارتان في الطريق: على حساب «سيارات في الطريق»
        var freight = await api.Post("/additionalcharges", new { documentType = "car_procurement_order", documentId = id, expenseTypeId = types["FREIGHT"], amount = 900, status = "actual", allocationMethod = "equal",
            allocations = (await api.Get($"/carprocurementorders/{id}/vehicles")).Data!["items"]!.AsArray().Select(v => new { vehicleId = v!["id"].S(), amount = 0 }).ToArray() });
        Assert.Equal(201, freight.Status);
        Assert.Equal(900, await BalanceAsync(api, "1143"));
        Assert.Equal(0, await BalanceAsync(api, "1142"));

        // الاستلام: النقل إلى المخزون بالتكلفة
        Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/AdvanceStage", new
        {
            targetStage = "vin_received", pdiInspectionPassed = true, vins = new[] { new { vin = Client.NewVin('2') }, new { vin = Client.NewVin('2') } },
        })).Status);
        Assert.Equal(0, await BalanceAsync(api, "1143"));
        Assert.Equal(900, await BalanceAsync(api, "1142"));

        // الفوترة: ثمن السيارات + رسوم الأمر الفعلية من حسابات الإعدادات
        Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = "invoiced" })).Status);
        Assert.Equal(161500, await BalanceAsync(api, "1142")); // 160000 + 900 شحن + 600 جمارك
        Assert.Equal(1500, await BalanceAsync(api, "212"));    // الشحن بلا مورد + الجمارك المستحقة

        // إلغاء مصروف رُحِّل والسيارة في الطريق ثم نُقل: الحسابان يتصفّران
        Assert.Equal(200, (await api.Post($"/additionalcharges/{freight.Data!["id"].S()}/cancel")).Status);
        Assert.Equal(0, await BalanceAsync(api, "1143"));
        Assert.Equal(160600, await BalanceAsync(api, "1142"));
        Assert.Equal(600, await BalanceAsync(api, "212"));
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Landed_cost_voucher_posts_one_entry_to_the_supplier_and_closes_the_replaced_accrual()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow, customsDutyFee = 1000,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 2, unitPrice = 80000 } },
        });
        var id = order.Data!["id"].S();
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = stage });
        await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = "vin_received", pdiInspectionPassed = true, vins = new[] { new { vin = Client.NewVin('3') }, new { vin = Client.NewVin('3') } } });
        await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = "invoiced" });
        Assert.Equal(161000, await BalanceAsync(api, "1142"));
        Assert.Equal(1000, await BalanceAsync(api, "212"));

        // فاتورة المخلّص: جمارك فعلية 1300 تحل محل المستحق 1000، ومخلّص 400 بضريبة 60
        var (broker, _) = await SeedSupplierAsync(api, "المخلّص");
        var voucher = await api.Post("/landedcostvouchers", new
        {
            supplierId = broker, supplierInvoiceNumber = "B-9", procurementOrderId = id,
            lines = new object[]
            {
                new { expenseTypeId = types["CUSTOMS_DUTY"], amount = 1300 },
                new { expenseTypeId = types["CUSTOMS_CLEARANCE"], amount = 400, vatAmount = 60 },
            },
        });
        var vid = voucher.Data!["id"].S();
        var approved = await api.Post($"/landedcostvouchers/{vid}/approve");
        Assert.Equal(200, approved.Status);
        Assert.NotNull(approved.Data!["journalEntryId"]);
        Assert.Equal(161700, await BalanceAsync(api, "1142")); // + 1300 + 400 − 1000 المستحق المستبدَل
        Assert.Equal(0, await BalanceAsync(api, "212"));       // أُقفل المستحق
        Assert.Equal(60, await BalanceAsync(api, "1131") - 24000); // ضريبة المخلّص فوق ضريبة فاتورة الشراء
        Assert.Equal(1760, (await api.Get($"/suppliers/{broker}")).Data!["currentBalance"].D());
        var vehicles = (await api.Get($"/carprocurementorders/{id}/vehicles")).Data!["items"]!.AsArray();
        Assert.Equal(161700, vehicles.Sum(v => v!["totalCost"].D())); // المخزون الدفتري = تكلفة السيارات

        // الإلغاء يعكس القيد ويعيد المستحق
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{vid}/cancel")).Status);
        Assert.Equal(161000, await BalanceAsync(api, "1142"));
        Assert.Equal(1000, await BalanceAsync(api, "212"));
        Assert.Equal(0, (await api.Get($"/suppliers/{broker}")).Data!["currentBalance"].D());
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Tamm_wallet_is_topped_up_charged_by_operations_and_reconciled()
    {
        var api = await NewTenantAsync();
        var (vid, _) = await SeedCarAsync(api);

        Assert.Equal(400, (await api.Post("/tammwallet/topup", new { amount = 0, treasuryAccountCode = "1112" })).Status);
        Assert.Equal(400, (await api.Post("/tammwallet/topup", new { amount = 100 })).Status);
        Assert.Equal(200, (await api.Post("/tammwallet/topup", new { amount = 1000, treasuryAccountCode = "1112", reference = "TR-1" })).Status);
        Assert.Equal(1000, await BalanceAsync(api, "119"));
        Assert.Equal(-1000, await BalanceAsync(api, "1112"));

        // نقل ملكية للمعرض: إضافة للتكلفة من رصيد تم
        var op = await api.Post("/tammoperations", new { type = "transfer_to_showroom", vehicleId = vid, fee = 250 });
        Assert.Equal(200, (await api.Post($"/tammoperations/{op.Data!["id"].S()}/status", new { status = "success" })).Status);
        Assert.Equal(750, await BalanceAsync(api, "119"));
        Assert.Equal(250, await BalanceAsync(api, "1142"));
        // الفشل والمجهول لا يخصمان من المحفظة
        var failed = await api.Post("/tammoperations", new { type = "hold", vehicleId = vid, fee = 40 });
        await api.Post($"/tammoperations/{failed.Data!["id"].S()}/status", new { status = "failed" });
        var unknown = await api.Post("/tammoperations", new { type = "hold", vehicleId = vid, fee = 40 });
        await api.Post($"/tammoperations/{unknown.Data!["id"].S()}/status", new { status = "unknown" });
        Assert.Equal(750, await BalanceAsync(api, "119"));

        // استعلام على سيارة لم تُشترَ: مصروف عام، ثم يُنقل لتكلفتها عند شرائها دون خصم ثانٍ من المحفظة
        var vin = Client.NewVin('8');
        var inquiry = await api.Post("/tammoperations", new { type = "violations_inquiry", vin, fee = 30 });
        await api.Post($"/tammoperations/{inquiry.Data!["id"].S()}/status", new { status = "success" });
        Assert.Equal(720, await BalanceAsync(api, "119"));
        Assert.Equal(30, await BalanceAsync(api, "529"));
        await SeedCarAsync(api, vin: vin);
        Assert.Equal(0, await BalanceAsync(api, "529"));
        Assert.Equal(280, await BalanceAsync(api, "1142"));
        Assert.Equal(720, await BalanceAsync(api, "119"));

        var wallet = (await api.Get("/tammwallet")).Data!;
        Assert.Equal("119", wallet["accountCode"].S());
        Assert.Equal(720, wallet["balance"].D());
        var movements = wallet["transactions"]!["items"]!.AsArray();
        Assert.Equal(3, movements.Count);
        Assert.Equal(720, movements.Sum(m => m!["amount"].D()));

        // المطابقة: الكشف 705 والدفتري 720 ← فرق −15 يُسجَّل، ويُسوّى على مصروف المعرض عند الطلب
        var noAdjust = await api.Post("/tammwallet/reconcile", new { statementBalance = 705 });
        Assert.Equal(-15, noAdjust.Data!["difference"].D()); Assert.Equal(720, noAdjust.Data["bookBalance"].D());
        Assert.Equal(720, await BalanceAsync(api, "119"));
        Assert.Equal(200, (await api.Post("/tammwallet/reconcile", new { statementBalance = 705, adjustDifference = true })).Status);
        Assert.Equal(705, await BalanceAsync(api, "119"));
        Assert.Equal(15, await BalanceAsync(api, "529"));
        Assert.Equal(0, (await api.Post("/tammwallet/reconcile", new { statementBalance = 705, adjustDifference = true })).Data!["difference"].D());
        Assert.Equal(705, (await api.Get("/tammwallet")).Data!["lastReconciliation"]!["statementBalance"].D());
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Customer_recharges_close_the_disbursement_account_and_add_ons_post_to_their_own_revenue()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (vid, _) = await SeedCarAsync(api);
        var (accessories, _) = await SeedSupplierAsync(api, "محل الزينة");
        var contract = await api.Post("/carsalescontracts", new { cycleType = "individual", buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000", vehicleId = vid, sellingPrice = 50000, paymentMethod = "cash", condition = "new" });
        var id = contract.Data!["id"].S();
        Task<Res> Line(string type, decimal amount, decimal? salePrice = null, string status = "estimated", Guid? supplierId = null)
            => api.Post("/additionalcharges", new { documentType = "car_sales_contract", documentId = id, expenseTypeId = types[type], amount, status, salePrice, supplierId });

        // رسم نقل الملكية على العميل (150 موعود)، وإضافة تكلفتها 800 وسعرها 1500
        await Line("TAMM_TRANSFER_OUT", 150);
        Assert.Equal(201, (await Line("CUSTOMER_ADDONS", 800, 1500, "actual", accessories)).Status);
        Assert.Equal(800, await BalanceAsync(api, "515")); // تكلفة الإضافات
        Assert.Equal(800, (await api.Get($"/suppliers/{accessories}")).Data!["currentBalance"].D());

        // نجاح نقل الملكية في تم برسم فعلي 172.5: مدفوع نيابة عن العميل من رصيد تم
        var op = await api.Post("/tammoperations", new { type = "transfer_to_buyer", vehicleId = vid, contractId = id, fee = 172.5 });
        await api.Post($"/tammoperations/{op.Data!["id"].S()}/status", new { status = "success" });
        Assert.Equal(150, await BalanceAsync(api, "116"));     // ما سيُفوتَر على العميل
        Assert.Equal(22.5m, await BalanceAsync(api, "529"));   // فرق الرسم الفعلي عن الموعود على المعرض
        Assert.Equal(-172.5m, await BalanceAsync(api, "119"));

        // الفاتورة: السيارة على إيراد السيارات، الإضافة على إيراد الإضافات، والرسم يقفل حساب المدفوعات نيابة عن العملاء (لا يدخل المبيعات)
        var done = await api.Post($"/carsalescontracts/{id}/complete");
        Assert.Equal(200, done.Status);
        Assert.Equal(59375, (await api.Get($"/invoices/{done.Data!["invoiceId"].S()}")).Data!["grandTotal"].D());
        Assert.Equal(50000, await BalanceAsync(api, "412"));
        Assert.Equal(1500, await BalanceAsync(api, "413"));
        Assert.Equal(0, await BalanceAsync(api, "116")); // أقفلته الفاتورة تماماً
        Assert.Equal(40000, await BalanceAsync(api, "512")); // تكلفة المبيعات بالتكلفة الكلية للسيارة
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Accounts_come_from_the_showroom_settings_not_from_code()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (vid, _) = await SeedCarAsync(api);
        Assert.Equal(201, (await api.Post("/accounts", new { code = "5299", nameAr = "مصاريف تسويق السيارات", nameEn = "Car Marketing", type = "expense", parentCode = "52", isDebitNature = true })).Status);

        var settings = (await api.Get("/carshowroomsettings")).Data!;
        Assert.Equal("1142", settings["vehicleInventoryAccount"].S()); Assert.Equal("119", settings["tammWalletAccount"].S());
        Assert.Equal("purchase_price", settings["marginBasis"].S());
        object Body(string? expenseAccount, string? inventory = null) => new
        {
            saleFeesBorneBy = "customer", reservationDays = 7, standardVatRate = 15, marginBasis = "purchase_price",
            vehicleExpenseAccount = expenseAccount, vehicleInventoryAccount = inventory,
        };
        Assert.Equal(400, (await api.Put("/carshowroomsettings", Body("77777"))).Status);   // غير موجود
        Assert.Equal(400, (await api.Put("/carshowroomsettings", Body(null, "114"))).Status); // حساب أب
        var saved = await api.Put("/carshowroomsettings", Body("5299"));
        Assert.Equal(200, saved.Status);
        Assert.Equal("5299", saved.Data!["vehicleExpenseAccount"].S());
        Assert.Equal("1142", saved.Data["vehicleInventoryAccount"].S()); // الفارغ يبقى كما هو

        Assert.Equal(201, (await ChargeAsync(api, vid, types["SALES_COMMISSION"], 400)).Status);
        Assert.Equal(400, await BalanceAsync(api, "5299"));
        Assert.Equal(0, await BalanceAsync(api, "529"));

        // حساب خاص بالنوع يتقدّم على حساب الإعدادات
        Assert.Equal(200, (await api.Put($"/expensetypes/{types["TAMM_HOLD"]}", new { code = "TAMM_HOLD", nameAr = "تحفظ", stage = "holding", defaultTreatment = "expense", defaultAllocationMethod = "equal", vatCategory = "out_of_scope", accountCode = "524" })).Status);
        var before = await BalanceAsync(api, "524");
        Assert.Equal(201, (await ChargeAsync(api, vid, types["TAMM_HOLD"], 60)).Status);
        Assert.Equal(before + 60, await BalanceAsync(api, "524"));
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Vat_rate_and_margin_basis_are_accountant_settings()
    {
        var api = await NewTenantAsync();
        // مستعملة بهامش الربح: شراء 50000 + تكاليف 1500، وبيع 60000
        var (used, _) = await SeedCarAsync(api, purchase: 50000, additional: 1500, selling: 60000, vatMode: "profit_margin_15", condition: "used");
        var (standard, _) = await SeedCarAsync(api, purchase: 40000, selling: 50000);
        JsonNode Vehicle(string id) => api.Get($"/vehicles/{id}").Result.Data!;
        async Task TouchAsync(string id, string vatMode, string condition, decimal selling)
        {
            var v = Vehicle(id);
            Assert.Equal(200, (await api.Put($"/vehicles/{id}", new { chassisNumber = v["chassisNumber"].S(), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition, mileageKm = v["mileageKm"]?.GetValue<decimal?>(),
                sellingPrice = selling, vatMode, fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض" })).Status);
        }

        // الافتراضي: الهامش من سعر الشراء فقط (المصاريف المُرسمَلة لا تخفّضه)، والنسبة 15%
        Assert.Equal(1304.35m, Vehicle(used)["vatAmount"].D());   // (60000 − 50000) × 15/115
        Assert.Equal(7500, Vehicle(standard)["vatAmount"].D());
        var contract = await api.Post("/carsalescontracts", new { cycleType = "individual", buyerName = "م", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000", vehicleId = used, sellingPrice = 60000, paymentMethod = "cash", condition = "used" });
        Assert.Equal(50000, contract.Data!["marginBaseCost"].D());
        Assert.Equal(51500, contract.Data["costPrice"].D());
        Assert.Equal(1304.35m, contract.Data["vatAmount"].D());
        Assert.Equal(8500, contract.Data["profitMargin"].D()); // الربح الفعلي: السعر − التكلفة الكلية

        // بيعها يُظهرها في تقرير المقارنة بين طريقتي الأساس
        Assert.Equal(200, (await api.Post($"/carsalescontracts/{contract.Data["id"].S()}/complete")).Status);
        var (plain, _) = await SeedCarAsync(api, purchase: 30000, selling: 36000, vatMode: "profit_margin_15", condition: "used");
        Assert.Equal(201, (await api.Post("/carsalescontracts/QuickSale", new { contract = new { cycleType = "individual", buyerName = "م", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000", vehicleId = plain, sellingPrice = 36000, paymentMethod = "cash", condition = "used" } })).Status);
        var comparison = (await api.Get("/reports/car/MarginBasisComparison")).Data!.AsArray();
        var row = comparison.Single()!; // السيارة بلا مصاريف مُرسمَلة لا يختلف هامشها
        Assert.Equal(50000, row["purchasePrice"].D()); Assert.Equal(51500, row["totalCost"].D());
        Assert.Equal(10000, row["marginOnPurchasePrice"].D()); Assert.Equal(1304.35m, row["vatOnPurchasePrice"].D());
        Assert.Equal(8500, row["marginOnTotalCost"].D()); Assert.Equal(1108.70m, row["vatOnTotalCost"].D());
        Assert.Equal(1304.35m, row["appliedVat"].D()); Assert.Equal(195.65m, row["vatDifference"].D());

        // المحاسب يغيّر الأساس إلى التكلفة الكلية والنسبة إلى 10%: الحسابات الجديدة تتبع الإعداد
        var (used2, _) = await SeedCarAsync(api, purchase: 50000, additional: 1500, selling: 60000, vatMode: "profit_margin_15", condition: "used");
        Assert.Equal(200, (await api.Put("/carshowroomsettings", new { saleFeesBorneBy = "customer", reservationDays = 7, standardVatRate = 10, marginBasis = "total_cost" })).Status);
        await TouchAsync(used2, "profit_margin_15", "used", 60000);
        Assert.Equal(772.73m, Vehicle(used2)["vatAmount"].D()); // (60000 − 51500) × 10/110
        await TouchAsync(standard, "standard_15", "new", 50000);
        Assert.Equal(5000, Vehicle(standard)["vatAmount"].D()); Assert.Equal(55000, Vehicle(standard)["priceWithVat"].D());
        Assert.Equal(5000, (await api.Post("/vehicles/CalculateVat", new { costPrice = 40000, sellingPrice = 50000, mode = "standard_15" })).Data!["vatAmount"].D());
    }
}
