using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;
using Xunit.Abstractions;

namespace ERP.Tests;

/// <summary>
/// سيناريو تجريبي كامل لمشروع وهمي «تركيب إنارة أعمدة — طريق الملك فهد» (50 عموداً) في وحدة المقاولات، من أول مرحلة لآخرها:
/// مكتبة التكاليف ← مكتبة البنود بأكواد قالب الإنارة ← استيراد جدول الكميات من القالب (ربط «رمز بند المكتبة» كما تفعل الواجهة)
/// ← دراسة الجدوى والتسعير ← خطة التنفيذ واعتمادها ← إنشاء العقد وتفعيله ← موارد المشروع ← الشراء والتوريد ← التسليم للموقع
/// ← التقارير اليومية واعتمادها ← مستخلص جارٍ ← مستخلص ختامي ← الاستلام الأولي ← الإفراج عن الضمان ← الإقفال.
/// كل خطوة تُكتب في مخرجات الاختبار ليتابعها المستخدم.
/// </summary>
[Collection("api")]
public class LightingPolesProjectJourneyTests : TestBase
{
    private readonly ITestOutputHelper _out;
    public LightingPolesProjectJourneyTests(ErpFactory f, ITestOutputHelper output) : base(f) => _out = output;

    private static readonly DateTime Today = DateTime.UtcNow.Date;
    private const string Revenue = "411";
    private const string OutputVat = "213";
    private const string RetentionReceivable = "1132";

    private int _step;
    private void Log(string message) => _out.WriteLine($"[{++_step:00}] {message}");

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private static async Task<decimal> BalanceAsync(Client api, string code)
        => (await api.Get("/reports/TrialBalance")).Data!.AsArray()
            .Where(r => r!["accountCode"].S() == code)
            .Sum(r => r!["openingDebit"].D() - r!["openingCredit"].D() + r!["periodDebit"].D() - r!["periodCredit"].D());

    /// <summary>صفوف ورقة «جدول_الكميات» في قالب مشروع الإنارة (رقم البند، رمز بند المكتبة، القسم، الوصف، النوع، الوحدة، الكمية، السعر).</summary>
    private static readonly (string LineCode, string LibraryCode, string Section, string Description, string Kind, string Unit, decimal Qty, decimal Price)[] TemplateBoq =
    {
        ("1", "LGT-W03", "قواعد الأعمدة", "تنفيذ قواعد خرسانية ومسامير التثبيت للأعمدة", "measured", "عدد", 50, 950),
        ("2", "LGT-W04", "الأعمدة والكشافات", "توريد وتركيب أعمدة الإنارة 9م مع كشافات LED 150W", "measured", "عدد", 50, 3850),
        ("3", "LGT-W05", "الكابلات", "سحب وتوصيل واختبار كابلات التغذية 4×16 مم2", "measured", "م", 2000, 48),
        ("4", "", "الفحص والتسليم", "اختبارات التشغيل والتسليم والمخططات حسب التنفيذ", "measured", "مقطوعية", 1, 8500),
    };

    [Fact]
    public async Task Lighting_poles_project_runs_from_cost_library_to_contract_closure()
    {
        // ================= المرحلة 0: المنشأة والبيانات الأساسية =================
        var api = await NewConstructionTenantAsync();
        foreach (var (code, name, type) in new[] { ("PCS", "عدد", "count"), ("M", "متر", "length"), ("HR", "ساعة", "time") })
            Ok(await api.Post("/unitsofmeasure", new { code, nameAr = name, unitType = type, isBaseUnit = true, conversionFactor = 1 }), "unit " + code);
        await api.Post("/productcategories", new { code = "LGT", nameAr = "مواد إنارة" });
        async Task<Guid> Product(string sku, string name, string unit)
        {
            var p = await api.Post("/products", new { sku, nameAr = name, category = "LGT", unit, sellingPrice = 1, vatRate = 15 });
            Ok(p, "product " + sku);
            return p.Data!["id"].G();
        }
        var poleProduct = await Product("POLE-9M", "عمود إنارة مجلفن 9م", "PCS");
        var ledProduct = await Product("LED-150", "كشاف LED 150W", "PCS");
        var cableProduct = await Product("CBL-4x16", "كابل نحاس 4×16 مم2", "M");
        var (supplier, _) = await SeedSupplierAsync(api, "مؤسسة النور للتوريدات الكهربائية");
        var (customer, customerAccount) = await SeedCustomerAsync(api, creditLimit: 2_000_000, name: "أمانة المنطقة — إدارة الطرق");
        Log("المنشأة: وحدات (عدد/متر/ساعة)، 3 أصناف مخزنية بلا رصيد، مورد «مؤسسة النور»، عميل «أمانة المنطقة».");

        // ================= المرحلة 1: مكتبة تكاليف المقاولات =================
        async Task<Guid> Resource(object body, string step)
        {
            var r = await api.Post("/construction-resources", body);
            Ok(r, step);
            return r.Data!["id"].G();
        }
        var pole = await Resource(new { code = "LGT-M02", nameAr = "عمود إنارة مجلفن 9م", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 1900, productId = poleProduct }, "pole");
        var led = await Resource(new { code = "LGT-M01", nameAr = "كشاف LED 150W", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 900, productId = ledProduct }, "led");
        var cable = await Resource(new { code = "LGT-M06", nameAr = "كابل نحاس 4×16 مم2", resourceType = "material", unit = "M", costSource = "manual", standardCost = 34, productId = cableProduct }, "cable");
        var electrician = await Resource(new
        {
            code = "LGT-L01", nameAr = "فني كهرباء", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, category = "كهرباء",
            breakdown = new { rateBasis = "hour", rate = 45, overtimeMultiplier = 1.5 },
        }, "electrician");
        var civilWorker = await Resource(new
        {
            code = "LGT-L02", nameAr = "عامل مدني", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, category = "مدني",
            breakdown = new { rateBasis = "hour", rate = 28, overtimeMultiplier = 1.5 },
        }, "civil worker");
        var crane = await Resource(new
        {
            code = "LGT-E02", nameAr = "رافعة سلة", resourceType = "equipment", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, equipmentOwnership = "owned",
            breakdown = new { rateBasis = "hour", rate = 260, overtimeMultiplier = 1 },
        }, "crane");
        var summary = (await api.Get("/construction-resources/cost-summary")).Data;
        Log($"مكتبة التكاليف: 3 مواد مربوطة بأصناف (عمود 1900، كشاف 900، كابل 34/م)، عمالة (فني 45/س، عامل 28/س)، معدة (رافعة 260/س). الملخص: {summary?.ToJsonString()}");

        // ================= المرحلة 2: مكتبة البنود بأكواد القالب =================
        async Task<Guid> WorkItem(object body, string step)
        {
            var r = await api.Post("/work-items", body);
            Ok(r, step);
            return r.Data!["id"].G();
        }
        await WorkItem(new
        {
            code = "LGT-W03", nameAr = "تنفيذ قواعد الأعمدة", unit = "PCS", pricingMode = "manual", sellPrice = 950, sectionName = "قواعد الأعمدة",
            components = new object[] { new { resourceId = civilWorker, quantityMode = "per_unit", consumptionFactor = 6, wastePercent = 0 } },
        }, "W03");
        await WorkItem(new
        {
            code = "LGT-W04", nameAr = "تركيب الأعمدة والكشافات", unit = "PCS", pricingMode = "manual", sellPrice = 3850, sectionName = "الأعمدة والكشافات",
            components = new object[]
            {
                new { resourceId = pole, quantityMode = "per_unit", consumptionFactor = 1, wastePercent = 0 },
                new { resourceId = led, quantityMode = "per_unit", consumptionFactor = 1, wastePercent = 0 },
                new { resourceId = crane, quantityMode = "per_unit", consumptionFactor = 0.5, wastePercent = 0 },
                new { resourceId = electrician, quantityMode = "per_unit", consumptionFactor = 2, wastePercent = 0 },
            },
        }, "W04");
        await WorkItem(new
        {
            code = "LGT-W05", nameAr = "سحب الكابلات والتوصيل", unit = "M", pricingMode = "manual", sellPrice = 48, sectionName = "الكابلات",
            components = new object[]
            {
                new { resourceId = cable, quantityMode = "per_unit", consumptionFactor = 1, wastePercent = 0 },
                new { resourceId = electrician, quantityMode = "per_unit", consumptionFactor = 0.1, wastePercent = 0 },
            },
        }, "W05");
        Log("مكتبة البنود: LGT-W03 قواعد (6 س عامل = 168)، LGT-W04 عمود+كشاف (1900+900+0.5×260+2×45 = 3020)، LGT-W05 كابل (34+0.1×45 = 38.5/م).");

        // ================= المرحلة 3: استيراد جدول الكميات من القالب =================
        // مثل importBoq في الواجهة: «رمز بند المكتبة» يُطابَق مع كود بند المكتبة النشط فيأخذ معرّفه وتكلفته
        var library = (await api.Get("/work-items")).Data!.AsArray()
            .Where(w => w!["isActive"]?.GetValue<bool>() != false)
            .ToDictionary(w => w!["code"].S().ToLowerInvariant(), w => w!);
        var items = TemplateBoq.Select((row, i) =>
        {
            library.TryGetValue(row.LibraryCode.ToLowerInvariant(), out var lib);
            return (object)new
            {
                lineCode = row.LineCode, sectionName = row.Section, description = row.Description, lineKind = row.Kind, unit = row.Unit,
                quantity = row.Qty, unitPrice = row.Price, vatRate = 15, sortOrder = i + 1,
                workItemId = lib?["id"].G(),
                estimatedUnitCost = lib is null ? 5000m : lib["unitCost"].D(),
            };
        }).ToArray();
        Assert.Equal(3, TemplateBoq.Count(r => library.ContainsKey(r.LibraryCode.ToLowerInvariant())));
        var contractValue = TemplateBoq.Sum(r => r.Qty * r.Price);
        Assert.Equal(344_500m, contractValue); // 47,500 + 192,500 + 96,000 + 8,500
        Log($"استيراد القالب: 4 بنود (3 مربوطة بالمكتبة + بند الفحص اليدوي)، قيمة الأعمال قبل الضريبة {contractValue:N0}.");

        // ================= المرحلة 4: دراسة الجدوى والتسعير =================
        var studyRes = await api.Post("/commercialcontracts", new
        {
            title = "تركيب إنارة أعمدة — طريق الملك فهد", projectName = "تركيب إنارة أعمدة", contractType = "construction_boq", workflow = "simple",
            requiresGuarantee = false, partyType = "customer", partyId = customer, partyName = "أمانة المنطقة — إدارة الطرق",
            startDate = Today, endDate = Today.AddDays(119), vatRate = 15, retentionPercent = 5,
            items, milestones = Array.Empty<object>(), clauses = Array.Empty<object>(), isFeasibilityStudy = true,
        });
        Ok(studyRes, "study");
        var id = studyRes.Data!["id"].S();
        Assert.Equal("study", studyRes.Data["feasibilityStatus"].S());
        var lines = studyRes.Data["items"]!.AsArray();
        Guid Line(string code) => lines.Single(l => l!["lineCode"].S() == code)!["id"].G();
        Guid bases = Line("1"), poles = Line("2"), cables = Line("3"), handover = Line("4");
        Ok(await api.Post($"/commercialcontracts/{id}/cost-analysis/apply", new { }), "cost analysis");
        var study = (await api.Get("/feasibility-studies")).Data!.AsArray().Single(s => s!["id"].S() == id)!;
        Assert.Equal(contractValue, study["contractValue"].D());
        Assert.True(study["margin"].D() > 0, study.ToJsonString());
        Log($"الدراسة {studyRes.Data["feasibilityNumber"].S()}: القيمة {study["contractValue"].D():N0}، التكلفة التقديرية {study["estimatedCost"].D():N0}، الهامش {study["margin"].D():N0} ({study["marginPercent"].D()}%).");

        // ================= المرحلة 5: خطة التنفيذ واعتمادها =================
        var plan = await api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { });
        Ok(plan, "plan generate");
        var phases = plan.Data!["phases"]!.AsArray().Select(p => p!["name"].S()).ToArray();
        Assert.Equal(new[] { "قواعد الأعمدة", "الأعمدة والكشافات", "الكابلات", "الفحص والتسليم" }, phases);
        var materials = plan.Data["materials"]!.AsArray();
        Assert.Equal(50m, materials.Where(m => m!["resourceId"].S() == pole.ToString()).Sum(m => m!["quantity"].D()));
        Assert.Equal(2000m, materials.Where(m => m!["resourceId"].S() == cable.ToString()).Sum(m => m!["quantity"].D()));
        Log($"خطة التنفيذ: المراحل [{string.Join(" ← ", phases)}]، المواد المخططة {materials.Count} سطر، الميزانية {plan.Data["budget"]!["plannedCost"].D():N0}.");
        // ميزانية المشروع تُعبّأ من تكلفة الدراسة (241,400)، والمخطط من الموارد 236,400 (البند اليدوي 5,000 بلا موارد) فيظهر تنبيه الفرق
        Assert.Equal(241_400m, plan.Data["budget"]!["estimatedBudget"].D());
        Assert.Equal(236_400m, plan.Data["budget"]!["plannedCost"].D());
        Assert.Contains(plan.Data["warnings"]!.AsArray(), w => w!["code"].S() == "budget_differs");
        Ok(await api.Post($"/commercialcontracts/{id}/execution-plan/submit", new { }), "plan submit");
        var approved = await api.Post($"/commercialcontracts/{id}/execution-plan/approve", new { });
        Ok(approved, "plan approve");
        Assert.Equal("plan_approved", approved.Data!["feasibilityStatus"].S());
        Assert.Equal("approved", approved.Data["materialPlanStatus"].S());
        Log("الخطة قُدّمت واعتُمدت؛ احتياجات المواد معتمدة تلقائياً من الخطة.");

        // ================= المرحلة 6: إنشاء العقد وتفعيله =================
        var contract = await api.Post($"/feasibility-studies/{id}/create-contract", new { });
        Ok(contract, "create contract");
        Assert.StartsWith("CON-", contract.Data!["contractNumber"].S());
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        Assert.Contains(id, (await api.Get("/construction-projects")).Data!.AsArray().Select(p => p!["contractId"].S()));
        Log($"العقد {contract.Data["contractNumber"].S()} أُنشئ من الدراسة وفُعّل (ساري)، وظهر في قائمة المشاريع.");

        // ================= المرحلة 7: إعداد المشروع وموارده =================
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "15:00" }), "work hours");
        async Task<Guid> ProjectResource(object body, string name)
        {
            var r = await api.Post($"/commercialcontracts/{id}/project/resources", body);
            Ok(r, name);
            return r.Data!.AsArray().First(x => x!["name"].S() == name)!["id"].G();
        }
        var crewElectric = await ProjectResource(new { kind = "labor", name = "فريق الكهرباء", rateBasis = "hour", rate = 45, hoursPerDay = 8, daysPerMonth = 26, overtimeMultiplier = 1.5, libraryResourceId = electrician }, "فريق الكهرباء");
        var crewCivil = await ProjectResource(new { kind = "labor", name = "فريق مدني", rateBasis = "hour", rate = 28, hoursPerDay = 8, daysPerMonth = 26, overtimeMultiplier = 1.5, libraryResourceId = civilWorker }, "فريق مدني");
        var craneRes = await ProjectResource(new { kind = "owned_equipment", name = "رافعة سلة 01", rate = 200, fuelPerHour = 40, maintenancePerHour = 10, operatorPerHour = 10, libraryResourceId = crane }, "رافعة سلة 01");
        Log("إعداد المشروع: ساعات العمل 07:00–15:00، وموارد فعلية: فريق الكهرباء 45/س، فريق مدني 28/س، رافعة سلة 260/س.");

        // ================= المرحلة 8: الشراء (طلب ← اعتماد ← فاتورة شراء مرحّلة) =================
        var requisition = await api.Post("/materialrequisitions", new
        {
            requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(7), department = "المشاريع", requestedBy = "م. سامي", priority = "high",
            supplierId = supplier, supplierName = "مؤسسة النور للتوريدات الكهربائية", status = "draft", contractId = id,
            items = new object[]
            {
                new { itemId = poleProduct, itemName = "عمود إنارة مجلفن 9م", sku = "POLE-9M", unit = "PCS", requestedQuantity = 50m, estimatedCost = 1850m },
                new { itemId = ledProduct, itemName = "كشاف LED 150W", sku = "LED-150", unit = "PCS", requestedQuantity = 50m, estimatedCost = 880m },
                new { itemId = cableProduct, itemName = "كابل نحاس 4×16 مم2", sku = "CBL-4x16", unit = "M", requestedQuantity = 2000m, estimatedCost = 33m },
            },
        });
        Ok(requisition, "requisition");
        var reqId = requisition.Data!["id"].S();
        Ok(await api.Post($"/materialrequisitions/{reqId}/approve"), "requisition approve");
        var purchase = await api.Post($"/materialrequisitions/{reqId}/ConvertToInvoice");
        Ok(purchase, "convert to purchase");
        Ok(await api.Post($"/purchase-invoices/{purchase.Data!["id"].S()}/post"), "purchase post");
        const decimal purchaseCost = 50 * 1850 + 50 * 880 + 2000 * 33; // 202,500
        Assert.Equal(purchaseCost, await BalanceAsync(api, MainSiteInventory));
        Log($"الشراء: طلب {requisition.Data["requisitionNumber"]?.ToString()} اعتُمد وحُوّل لفاتورة شراء مرحّلة؛ المخزون {purchaseCost:N0}.");

        // ================= المرحلة 9: إذن تسليم أول للموقع (نصف المشروع) =================
        async Task<JsonNode> Deliver(decimal polesQty, decimal cableQty, string step)
        {
            var r = await api.Post($"/commercialcontracts/{id}/material-issues", new
            {
                receiverType = "site_engineer", receiverName = "م. سامي", post = true,
                lines = new object[]
                {
                    new { boqItemId = poles, productId = poleProduct, quantity = polesQty },
                    new { boqItemId = poles, productId = ledProduct, quantity = polesQty },
                    new { boqItemId = cables, productId = cableProduct, quantity = cableQty },
                },
            });
            Ok(r, step);
            return r.Data!;
        }
        var issue1 = await Deliver(25, 1000, "issue 1");
        Assert.Equal(25 * 1850m + 25 * 880m + 1000 * 33m, issue1["totalCost"].D()); // 101,250
        Log($"إذن التسليم {issue1["issueNumber"].S()}: 25 عموداً + 25 كشافاً + 1000 م كابل، بتكلفة {issue1["totalCost"].D():N0} على مواد تحت التنفيذ.");

        // ================= المرحلة 10: التقرير اليومي الأول واعتماده =================
        async Task<string> DailyReport(DateTime day, decimal basesQty, decimal polesQty, decimal cableQty, string step)
        {
            var r = await api.Post($"/commercialcontracts/{id}/site-reports", new
            {
                reportDate = day,
                hours = new object[]
                {
                    new { start = "07:00", end = "09:00", contractItemId = bases, actualQuantity = basesQty },
                    new { start = "09:00", end = "12:00", contractItemId = poles, actualQuantity = polesQty },
                    new { start = "12:00", end = "15:00", contractItemId = cables, actualQuantity = cableQty },
                },
                resources = new object[]
                {
                    new { projectResourceId = crewCivil, contractItemId = bases, hours = 8 },
                    new { projectResourceId = craneRes, contractItemId = poles, hours = 6 },
                    new { projectResourceId = crewElectric, contractItemId = poles, hours = 5 },
                    new { projectResourceId = crewElectric, contractItemId = cables, hours = 3 },
                },
            });
            Ok(r, step);
            var reportId = r.Data!["id"].S();
            Ok(await api.Post($"/commercialcontracts/{id}/site-reports/{reportId}/submit", new { }), step + " submit");
            Ok(await api.Post($"/commercialcontracts/{id}/site-reports/{reportId}/approve", new { }), step + " approve");
            var s = r.Data["summary"]!;
            Log($"التقرير اليومي {day:yyyy-MM-dd}: قواعد {basesQty}، أعمدة {polesQty}، كابل {cableQty} م؛ تكلفة الموارد {s["resourceCost"].D():N2}، قُدّم واعتُمد.");
            return reportId;
        }
        await DailyReport(Today, 50, 25, 1000, "report 1");
        var costs1 = (await api.Get($"/commercialcontracts/{id}/project/cost-report")).Data!;
        // حفظ ساعات العمل وحدها (المرحلة 7) لا يصفّر الميزانية المعبّأة من الدراسة
        Assert.Equal(241_400m, costs1["projectBudget"].D());
        Assert.Equal(241_400m, costs1["budgetAtCompletion"].D());
        Log($"تقرير التكلفة بعد الاعتماد: {costs1.ToJsonString()[..Math.Min(400, costs1.ToJsonString().Length)]}…");

        // ================= المرحلة 11: المستخلص الجاري الأول =================
        async Task<JsonNode> Certificate(string kind, (Guid Item, decimal Qty)[] work, JsonNode issue, string step)
        {
            var materialLines = issue["lines"]!.AsArray().Select(l => new { issueLineId = l!["id"].G(), quantity = l["quantity"].D() }).ToArray();
            var cert = await api.Post($"/commercialcontracts/{id}/certificates", new
            {
                kind, certificateDate = DateTime.UtcNow.Date,
                lines = work.Select(w => new { contractItemId = w.Item, currentQuantity = w.Qty }).ToArray(),
                deductions = Array.Empty<object>(), materials = materialLines,
            });
            Ok(cert, step);
            var certId = cert.Data!["id"].S();
            await AttachCertificateEvidenceAsync(api, id, certId);
            Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/submit"), step + " submit");
            Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/approve"), step + " approve");
            Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/bill"), step + " bill");
            var c = (await api.Get($"/commercialcontracts/{id}/certificates/{certId}")).Data!;
            Log($"مستخلص {kind} {c["certificateNumber"]?.ToString()}: الأعمال {c["currentWorkValue"].D():N2}، الضريبة {c["vatAmount"].D():N2}، المحتجز {c["retentionAmount"].D():N2}، الصافي {c["netPayable"].D():N2}، تكلفة المواد {c["materialCost"].D():N2} — الحالة {c["status"].S()}.");
            return c;
        }
        // 50 قاعدة × 950 + 25 عموداً × 3850 + 1000 م × 48 = 47,500 + 96,250 + 48,000 = 191,750
        var c1 = await Certificate("interim", new[] { (bases, 50m), (poles, 25m), (cables, 1000m) }, issue1, "certificate 1");
        Assert.Equal(191_750m, c1["currentWorkValue"].D());
        Assert.Equal(9_587.5m, c1["retentionAmount"].D());
        Assert.Equal(issue1["totalCost"].D(), c1["materialCost"].D());
        var progress1 = (await api.Get($"/commercialcontracts/{id}/boq-progress")).Data!;
        Log($"نسبة الإنجاز بعد المستخلص الأول: {progress1["completionPercent"].D()}%.");

        // ================= المرحلة 12: النصف الثاني ثم المستخلص الختامي =================
        var issue2 = await Deliver(25, 1000, "issue 2");
        Log($"إذن التسليم {issue2["issueNumber"].S()}: باقي المواد بتكلفة {issue2["totalCost"].D():N0}.");
        await DailyReport(Today.AddDays(1), 0, 25, 1000, "report 2");
        // 25 × 3850 + 1000 × 48 + 1 × 8500 = 96,250 + 48,000 + 8,500 = 152,750 (الصفر للقواعد المنجزة)
        var c2 = await Certificate("final", new[] { (poles, 25m), (cables, 1000m), (handover, 1m) }, issue2, "final certificate");
        Assert.Equal(152_750m, c2["currentWorkValue"].D());
        var progress = (await api.Get($"/commercialcontracts/{id}/boq-progress")).Data!;
        Assert.Equal(contractValue, progress["certifiedValue"].D());
        Assert.Equal(100m, progress["completionPercent"].D());
        Log($"بعد الختامي: المعتمد {progress["certifiedValue"].D():N0} = قيمة العقد، الإنجاز {progress["completionPercent"].D()}%.");

        // ================= المرحلة 13: الاستلام والإفراج عن المحتجز والإقفال =================
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "initial_acceptance" }), "initial acceptance");
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "closed" })).Status);
        Log("الاستلام الأولي تم؛ الإقفال مرفوض قبل الإفراج عن المحتجز (كما هو متوقع).");
        var release = await api.Post($"/commercialcontracts/{id}/retention-release");
        Ok(release, "retention release");
        Assert.Equal(contractValue * 0.05m, release.Data!["amount"].D());
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "closed" }), "close");
        Assert.Equal("closed", (await api.Get($"/commercialcontracts/{id}")).Data!["stage"].S());
        Log($"أُفرج عن المحتجز {release.Data["amount"].D():N2} وأُقفل العقد.");

        // ================= الحصيلة المحاسبية =================
        Assert.Equal(-contractValue, await BalanceAsync(api, Revenue));
        Assert.Equal(-contractValue * 0.15m, await BalanceAsync(api, OutputVat));
        Assert.Equal(contractValue * 1.15m, await BalanceAsync(api, customerAccount));
        Assert.Equal(0m, await BalanceAsync(api, RetentionReceivable));
        Assert.Equal(0m, await BalanceAsync(api, MainSiteInventory)); // كل المشتريات سُلّمت للموقع
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
        var finalCosts = (await api.Get($"/commercialcontracts/{id}/project/cost-report")).Data!;
        Log($"الحصيلة: الإيراد {contractValue:N0}، الضريبة {contractValue * 0.15m:N0}، ذمة العميل {contractValue * 1.15m:N0}، المخزون 0، الميزان متوازن ({d:N2}).");
        Log($"تقرير التكلفة والربحية النهائي: {finalCosts.ToJsonString()[..Math.Min(600, finalCosts.ToJsonString().Length)]}…");

        // ================= المطابقة: التقرير ↔ القيود ↔ مركز التكلفة =================
        var siteCost = finalCosts["siteOperatingCost"].D();
        Assert.Equal(2 * (8 * 28m + 8 * 45m + 6 * 260m), siteCost);                       // 4,288 من تقريرين معتمدين
        Assert.Equal(0, finalCosts["unpostedSiteReports"]!.GetValue<int>());
        Assert.Equal(contractValue, finalCosts["bookedRevenue"].D());                       // بلا ضريبة
        Assert.Equal(purchaseCost + siteCost, finalCosts["actualCost"].D());                // 206,788
        Assert.Equal(finalCosts["actualCost"].D(), finalCosts["expensedCost"].D());
        Assert.Equal(0m, finalCosts["wipBalance"].D());
        Assert.Equal(0m, finalCosts["duplicateExcluded"].D());
        Assert.Equal(contractValue - purchaseCost - siteCost, finalCosts["bookedProfit"].D()); // 137,712
        Assert.Equal(finalCosts["actualCost"].D(), finalCosts["accounts"]!.AsArray().Sum(a => a!["amount"].D()));
        JsonNode Cat(string key) => finalCosts["categories"]!.AsArray().Single(x => x!["key"].S() == key)!;
        Assert.Equal(purchaseCost, Cat("materials")["actual"].D());
        Assert.Equal(208_000m, Cat("materials")["planned"].D());
        Assert.Equal(siteCost, Cat("execution")["actual"].D());
        Assert.Equal(33_400m, Cat("execution")["planned"].D());                             // 28,400 عمالة ومعدات + 5,000 البند اليدوي
        // الدفاتر: المواد تُعترف مرة واحدة (مخزون ← تحت التنفيذ ← تكلفة الإيرادات)، وضريبة المدخلات خارج التكلفة
        Assert.Equal(purchaseCost, await BalanceAsync(api, "534"));
        Assert.Equal(0m, await BalanceAsync(api, "1134"));
        Assert.Equal(siteCost, await BalanceAsync(api, "535") + await BalanceAsync(api, "536"));
        Assert.Equal(-siteCost, await BalanceAsync(api, "537"));                           // مقابل التحميل الداخلي للموارد
        Assert.Equal(purchaseCost * 0.15m, await BalanceAsync(api, "1131"));
        // جسر الهامش: 137,712 − 103,100 = وفر أسعار المواد 5,500 + (المخطط 33,400 − الفعلي 4,288) للتنفيذ
        Assert.Equal(finalCosts["bookedProfit"].D() - study["margin"].D(),
            (Cat("materials")["planned"].D() - Cat("materials")["actual"].D()) + (Cat("execution")["planned"].D() - Cat("execution")["actual"].D()));
        foreach (var r in (await api.Get("/reports/TrialBalance")).Data!.AsArray())
        {
            var net = r!["openingDebit"].D() - r["openingCredit"].D() + r["periodDebit"].D() - r["periodCredit"].D();
            if (net != 0) _out.WriteLine($"TB|{r["accountCode"].S()}|{r["accountNameAr"].S()}|{net:N2}");
        }
    }
}
