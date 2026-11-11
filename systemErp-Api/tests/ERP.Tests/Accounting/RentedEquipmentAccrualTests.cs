using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// استحقاق إيجار المعدات المستأجرة من التقارير اليومية وتسويته بفواتير المؤجر: لا تُحتسب التكلفة مرتين في أي ترتيب
/// (الفاتورة بعد التقرير أو قبله، جزئية أو زائدة أو ناقصة، تعديل/حذف الفاتورة، إعادة فتح التقرير)، والضريبة على الفاتورة وحدها.
/// الآلية: 100 ريال/ساعة، التوقف بنسبة 50٪ والاستعداد بنسبة 25٪ في عقد إيجارها.
/// </summary>
[Collection("api")]
public class RentedEquipmentAccrualTests : TestBase
{
    public RentedEquipmentAccrualTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Day = new(2026, 11, 1);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Setup(Client Api, string Id, Guid Concrete, Guid Machine, Guid Labor, Guid Supplier, string SupplierAccount, Guid Center);

    private async Task<Setup> SetupAsync()
    {
        var api = await NewTenantWithModulesAsync("accounting", "construction");
        var (supplier, supplierAccount) = await SeedSupplierAsync(api, "مؤجر معدات");
        var created = await api.Post("/commercialcontracts", new
        {
            title = "مشروع", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل",
            startDate = Day.AddDays(-10), endDate = Day.AddMonths(6), vatRate = 15,
            items = new[] { new { description = "حفر", unit = "م3", quantity = 1000, unitPrice = 50, vatRate = 15, lineKind = "measured" } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "contract");
        var id = created.Data!["id"].S();
        var concrete = created.Data["items"]!.AsArray()[0]!["id"].G();
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "17:00" }), "profile");
        var center = (await api.Get($"/commercialcontracts/{id}")).Data!["costCenterId"].G();

        var machine = await api.Post($"/commercialcontracts/{id}/project/resources", new
        {
            kind = "rented_equipment", name = "حفار مستأجر", rateBasis = "hour", rate = 100, supplierId = supplier,
            idleBillingPercent = 50, standbyBillingPercent = 25,
        });
        Ok(machine, "machine");
        var labor = await api.Post($"/commercialcontracts/{id}/project/resources", new { kind = "labor", name = "عمال", rateBasis = "hour", rate = 25 });
        Ok(labor, "labor");
        var list = labor.Data!.AsArray();
        Guid IdOf(string name) => list.First(r => r!["name"].S() == name)!["id"].G();
        return new Setup(api, id, concrete, IdOf("حفار مستأجر"), IdOf("عمال"), supplier, supplierAccount, center);
    }

    private static object ReportBody(Setup s, DateTime date, decimal hours, decimal idle = 0, decimal standby = 0, decimal laborHours = 0)
    {
        var resources = new List<object>
        {
            new { projectResourceId = s.Machine, contractItemId = s.Concrete, hours, idleHours = idle, idleReason = idle > 0 ? "weather" : null, standbyHours = standby },
        };
        if (laborHours > 0) resources.Add(new { projectResourceId = s.Labor, contractItemId = s.Concrete, hours = laborHours });
        return new
        {
            reportDate = date,
            hours = new[] { new { start = "07:00", end = "08:00", contractItemId = s.Concrete, targetQuantity = 10, actualQuantity = 10 } },
            resources,
        };
    }

    private static async Task<string> ApprovedReportAsync(Setup s, DateTime date, decimal hours, decimal idle = 0, decimal standby = 0, decimal laborHours = 0)
    {
        var r = await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports", ReportBody(s, date, hours, idle, standby, laborHours));
        Ok(r, "report");
        var rid = r.Data!["id"].S();
        Ok(await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/{rid}/submit"), "submit");
        Ok(await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/{rid}/approve"), "approve");
        return rid;
    }

    private static async Task<string> RentalInvoiceAsync(Setup s, decimal net)
    {
        var inv = await s.Api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = s.Supplier, status = "posted", costCenterId = s.Center,
            items = new[] { new { itemName = "إيجار حفار", quantity = 1, unitPrice = net, vatRate = 15 } },
        });
        Ok(inv, "rental invoice");
        return inv.Data!["id"].S();
    }

    private static async Task<decimal> BalanceAsync(Client api, string code)
    {
        var rows = (await api.Get("/reports/TrialBalance")).Data!.AsArray();
        return rows.Where(r => r!["accountCode"].S() == code).Sum(r => r!["closingDebit"].D() - r!["closingCredit"].D());
    }

    private static async Task<decimal> AccruedBalanceAsync(Client api)
        => await BalanceAsync(api, (await api.Get("/accounting-settings")).Data!["projectAccruedRentalAccountCode"].S());

    private static async Task<decimal> CategoryAsync(Setup s, string key)
    {
        var report = await s.Api.Get($"/commercialcontracts/{s.Id}/project/cost-report");
        Ok(report, "cost report");
        return report.Data!["categories"]!.AsArray().Where(c => c!["key"].S() == key).Sum(c => c!["actual"].D());
    }

    private static async Task<JsonArray> AccrualsAsync(Setup s)
    {
        var r = await s.Api.Get($"/commercialcontracts/{s.Id}/site-reports/rental-accruals");
        Ok(r, "accruals");
        return r.Data!.AsArray();
    }

    private static async Task AssertBooksBalancedAsync(Client api)
    {
        var rows = (await api.Get("/reports/TrialBalance")).Data!.AsArray();
        Assert.Equal(rows.Sum(r => r!["closingDebit"].D()), rows.Sum(r => r!["closingCredit"].D()));
    }

    // ---------- الاستحقاق وشروط الفوترة ----------

    [Fact]
    public async Task Approval_accrues_by_billing_rules_and_matching_invoice_settles_without_double_counting()
    {
        var s = await SetupAsync();
        // 6 تشغيل + 2 توقف بنسبة 50٪ + 4 استعداد بنسبة 25٪ = 600 + 100 + 100
        var first = await ApprovedReportAsync(s, Day, 6, idle: 2, standby: 4);
        await ApprovedReportAsync(s, Day.AddDays(1), 5);

        var report = await s.Api.Get($"/commercialcontracts/{s.Id}/site-reports/{first}");
        Assert.Equal(800, report.Data!["resources"]!.AsArray()[0]!["cost"].D());
        var accruals = await AccrualsAsync(s);
        Assert.Equal(new[] { 800m, 500m }, accruals.Select(a => a!["amount"].D()).OrderByDescending(x => x));
        Assert.Equal(-1300, await AccruedBalanceAsync(s.Api));          // التزام بلا ذمة مورد
        Assert.Equal(0, await BalanceAsync(s.Api, s.SupplierAccount));
        Assert.Equal(1300, await CategoryAsync(s, "rented_equipment"));

        await RentalInvoiceAsync(s, 1300);
        accruals = await AccrualsAsync(s);
        Assert.All(accruals, a => Assert.Equal(0, a!["openAmount"].D()));
        Assert.Equal(1300, accruals.Sum(a => a!["settledAmount"].D()));
        Assert.Equal(0, await AccruedBalanceAsync(s.Api));
        Assert.Equal(-1495, await BalanceAsync(s.Api, s.SupplierAccount)); // الذمة والضريبة من الفاتورة وحدها
        Assert.Equal(1300, await CategoryAsync(s, "rented_equipment"));      // مرة واحدة لا 2600
        await AssertBooksBalancedAsync(s.Api);
    }

    [Fact]
    public async Task Partial_and_excess_invoices_and_invoice_before_report_are_counted_once()
    {
        var s = await SetupAsync();
        await ApprovedReportAsync(s, Day, 8);                                   // 800
        await RentalInvoiceAsync(s, 500);                                       // جزئية
        Assert.Equal(300, (await AccrualsAsync(s)).Sum(a => a!["openAmount"].D()));
        Assert.Equal(800, await CategoryAsync(s, "rented_equipment"));

        await RentalInvoiceAsync(s, 600);                                       // تتجاوز المستحق بـ 300
        Assert.Equal(0, (await AccrualsAsync(s)).Sum(a => a!["openAmount"].D()));
        Assert.Equal(1100, await CategoryAsync(s, "rented_equipment"));         // الفوترة الفعلية
        Assert.Equal(0, await AccruedBalanceAsync(s.Api));

        // تقرير لاحق لفترة فُوترت مقدماً: يُطابَق تلقائياً بالزيادة فلا يُضاف فوقها
        await ApprovedReportAsync(s, Day.AddDays(1), 5);                        // 500
        var accruals = await AccrualsAsync(s);
        Assert.Equal(1100, accruals.Sum(a => a!["settledAmount"].D()));
        Assert.Equal(200, accruals.Sum(a => a!["openAmount"].D()));
        Assert.Equal(1300, await CategoryAsync(s, "rented_equipment"));         // max(المستحق، الفواتير)
        Assert.Equal(-200, await AccruedBalanceAsync(s.Api));
        await AssertBooksBalancedAsync(s.Api);
    }

    [Fact]
    public async Task Lower_invoice_is_closed_and_deleting_the_invoice_unsettles_it()
    {
        var s = await SetupAsync();
        await ApprovedReportAsync(s, Day, 8);                                   // 800
        var invoice = await RentalInvoiceAsync(s, 700);
        Assert.Equal(100, (await AccrualsAsync(s)).Sum(a => a!["openAmount"].D()));

        Assert.Equal(400, (await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/rental-accruals/close", new { supplierId = s.Supplier })).Status);
        var closed = await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/rental-accruals/close", new { supplierId = s.Supplier, note = "خصم المؤجر" });
        Ok(closed, "close");
        Assert.Equal(100, closed.Data!.D());
        Assert.Equal(0, await AccruedBalanceAsync(s.Api));
        Assert.Equal(700, await CategoryAsync(s, "rented_equipment"));          // الفوترة الفعلية
        Assert.Equal(400, (await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/rental-accruals/close", new { supplierId = s.Supplier, note = "x" })).Status);

        // حذف الفاتورة: يُعكس قيدها وتُفك مطابقتها فيعود المستحق غير المقفل وحده تكلفة
        Ok(await s.Api.Delete(await s.Api.InvoicePath(invoice)), "delete invoice");
        var accruals = await AccrualsAsync(s);
        Assert.Equal(0, accruals.Sum(a => a!["settledAmount"].D()));
        Assert.Equal(700, accruals.Sum(a => a!["openAmount"].D()));
        Assert.Equal(-700, await AccruedBalanceAsync(s.Api));
        Assert.Equal(700, await CategoryAsync(s, "rented_equipment"));
        await AssertBooksBalancedAsync(s.Api);
    }

    // ---------- إعادة فتح التقرير المعتمد ----------

    [Fact]
    public async Task Reopened_settled_report_posts_only_the_difference_once()
    {
        var s = await SetupAsync();
        var rid = await ApprovedReportAsync(s, Day, 8, laborHours: 2);         // مستأجرة 800، عمالة 50
        await RentalInvoiceAsync(s, 800);
        Assert.Equal(50, await CategoryAsync(s, "labor"));

        Assert.Equal(400, (await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/{rid}/reopen", new { })).Status);
        var reopened = await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/{rid}/reopen", new { note = "تصحيح الساعات" });
        Ok(reopened, "reopen");
        Assert.Equal("draft", reopened.Data!["status"].S());
        Assert.Equal(1, reopened.Data["costRevision"]!.GetValue<int>());
        Assert.Equal(409, (await s.Api.Delete($"/commercialcontracts/{s.Id}/site-reports/{rid}")).Status); // قيوده قائمة
        // أثناء الفتح تبقى القيود الأصلية كما هي
        Assert.Equal(800, await CategoryAsync(s, "rented_equipment"));

        Ok(await s.Api.Put($"/commercialcontracts/{s.Id}/site-reports/{rid}", ReportBody(s, Day, 5, laborHours: 4)), "edit");
        Ok(await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/{rid}/submit"), "resubmit");
        var approved = await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/{rid}/approve");
        Ok(approved, "reapprove");
        Assert.Equal(2, approved.Data!["costRevision"]!.GetValue<int>());
        Assert.Equal(409, (await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports/{rid}/approve")).Status); // لا اعتماد مكرر

        var accrual = Assert.Single(await AccrualsAsync(s));
        Assert.Equal(500, accrual!["amount"].D());
        Assert.Equal(500, accrual["settledAmount"].D());                       // فُك 300 من المطابقة
        Assert.Equal(0, await AccruedBalanceAsync(s.Api));
        Assert.Equal(800, await CategoryAsync(s, "rented_equipment"));          // الفاتورة الفعلية (800) لا 500 ولا 1300
        Assert.Equal(100, await CategoryAsync(s, "labor"));                     // الفرق 50 فقط فوق الأصلي
        await AssertBooksBalancedAsync(s.Api);
    }

    // ---------- التحقق ----------

    [Fact]
    public async Task Standby_is_for_rented_equipment_only_and_billing_percentages_are_bounded()
    {
        var s = await SetupAsync();
        var bad = await s.Api.Post($"/commercialcontracts/{s.Id}/site-reports", new
        {
            reportDate = Day,
            hours = new[] { new { start = "07:00", end = "08:00", contractItemId = s.Concrete, targetQuantity = 10, actualQuantity = 10 } },
            resources = new[] { new { projectResourceId = s.Labor, contractItemId = s.Concrete, hours = 1, standbyHours = 2 } },
        });
        Assert.Equal(400, bad.Status);
        Assert.Equal(400, (await s.Api.Post($"/commercialcontracts/{s.Id}/project/resources", new
        {
            kind = "rented_equipment", name = "رافعة", rateBasis = "hour", rate = 100, supplierId = s.Supplier, idleBillingPercent = 120,
        })).Status);
    }
}
