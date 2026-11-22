using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// حجز مخزون المستودع للمشاريع: المتاح = الرصيد − المحجوز، ولا يُحجز فوق احتياج المشروع ولا فوق المتاح، ولا يُصرف لمشروع ما هو محجوز
/// لغيره، وصرف المشروع يستهلك حجزه وإلغاؤه يعيده، وتحويل التخصيص لا ينفذ إلا باعتماد غير مقدّمه. وربط أعضاء المشروع بالموظفين.
/// </summary>
[Collection("api")]
public class ProjectStockReservationTests : TestBase
{
    public ProjectStockReservationTests(ErpFactory f) : base(f) { }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");
    private static void Rejected(Res r, string step, params int[] codes)
        => Assert.True((codes.Length == 0 ? new[] { 400, 409 } : codes).Contains(r.Status), $"{step}: expected rejection, got {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Project(string Id, Guid Item);

    /// <summary>عقد مقاولات ساري ببند واحد واحتياج معتمد من مادة مربوطة بالصنف.</summary>
    private static async Task<Project> ProjectAsync(Client api, Guid resource, decimal required, string title)
    {
        var (customer, _) = await SeedCustomerAsync(api, name: "عميل " + title);
        var created = await api.Post("/commercialcontracts", new
        {
            title, contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyType = "customer", partyId = customer, partyName = "عميل",
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(6), vatRate = 15,
            items = new object[] { new { description = "صب", unit = "م3", quantity = 10, unitPrice = 100, vatRate = 15, lineKind = "measured", sortOrder = 1 } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "contract " + title);
        var id = created.Data!["id"].S();
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        var item = created.Data["items"]!.AsArray()[0]!["id"].G();
        Ok(await api.Put($"/commercialcontracts/{id}/material-requirements", new
        {
            lines = new object[] { new { boqItemId = item, resourceId = resource, source = "manual", baselineQuantity = required } },
        }), "plan");
        Ok(await api.Post($"/commercialcontracts/{id}/material-requirements/submit"), "plan submit");
        Ok(await api.Post($"/commercialcontracts/{id}/material-requirements/approve"), "plan approve");
        return new Project(id, item);
    }

    private static Task<Res> DeliverAsync(Client api, Project p, Guid product, decimal qty)
        => api.Post($"/commercialcontracts/{p.Id}/material-issues", new
        {
            receiverType = "site_engineer", receiverName = "م. خالد", post = true,
            lines = new[] { new { boqItemId = p.Item, productId = product, quantity = qty } },
        });

    private static Task<Res> ReserveAsync(Client api, Project p, Guid product, decimal qty)
        => api.Post($"/commercialcontracts/{p.Id}/stock-reservations", new { productId = product, quantity = qty });

    private static async Task<JsonNode> AvailabilityAsync(Client api, Project p, Guid product)
        => (await api.Get($"/commercialcontracts/{p.Id}/stock-availability?productIds={product}")).Data!.AsArray().Single()!;

    [Fact]
    public async Task Reservations_block_other_projects_are_consumed_by_issues_and_transfer_needs_another_approver()
    {
        var api = await NewConstructionTenantAsync();
        var product = await SeedProductAsync(api, "CEM", stock: 30, cost: 50);
        var resource = await api.Post("/construction-resources", new { nameAr = "أسمنت", resourceType = "material", unit = "PCS", costSource = "product", productId = product });
        Ok(resource, "resource");
        var a = await ProjectAsync(api, resource.Data!["id"].G(), 20, "مشروع أ");
        var b = await ProjectAsync(api, resource.Data!["id"].G(), 5, "مشروع ب");
        var c = await ProjectAsync(api, resource.Data!["id"].G(), 30, "مشروع ج");

        // لا حجز فوق احتياج المشروع المعتمد
        Rejected(await ReserveAsync(api, b, product, 6), "reserve above requirement");
        var reservedA = await ReserveAsync(api, a, product, 20);
        Ok(reservedA, "reserve A");
        Ok(await ReserveAsync(api, b, product, 5), "reserve B");
        // المتاح الآن 30 − 25 = 5: حجز 6 لمشروع ثالث مرفوض، والمكرر للمشروع نفسه مرفوض (المحجوز = المطلوب)
        Rejected(await ReserveAsync(api, c, product, 6), "reserve above available");
        Rejected(await ReserveAsync(api, a, product, 1), "duplicate reservation above requirement");

        var avail = await AvailabilityAsync(api, c, product);
        Assert.Equal(30m, avail["onHand"].D());
        Assert.Equal(25m, avail["reserved"].D());
        Assert.Equal(5m, avail["available"].D());

        // المحجوز لغيره لا يُصرف: لمشروع ج 5 فقط
        Rejected(await DeliverAsync(api, c, product, 6), "issue reserved stock to another project");
        Ok(await DeliverAsync(api, c, product, 5), "issue free stock");

        // صرف ب يستهلك حجزه، والإلغاء يعيده
        var issueB = await DeliverAsync(api, b, product, 5);
        Ok(issueB, "issue B");
        var listB = (await api.Get($"/commercialcontracts/{b.Id}/stock-reservations")).Data!.AsArray();
        Assert.Equal(0m, listB.Sum(r => r!["remainingQuantity"].D()));
        Assert.Equal("closed", listB[0]!["status"].S());
        Ok(await api.Post($"/commercialcontracts/{b.Id}/material-issues/{issueB.Data!["id"].S()}/cancel", new { reason = "خطأ" }), "cancel B");
        listB = (await api.Get($"/commercialcontracts/{b.Id}/stock-reservations")).Data!.AsArray();
        Assert.Equal(5m, listB.Sum(r => r!["remainingQuantity"].D()));
        Assert.Equal("active", listB[0]!["status"].S());

        avail = await AvailabilityAsync(api, a, product);
        Assert.Equal(25m, avail["onHand"].D()); // 30 − 5 لمشروع ج
        Assert.Equal(0m, avail["issued"].D()); // المصروف لمشروع أ نفسه
        Assert.Equal(20m, avail["reservedForProject"].D());
        Assert.Equal(20m, avail["availableForProject"].D());

        // تحويل 4 من أ إلى ج: طلب لا ينفذ قبل الاعتماد، ومقدّمه لا يعتمده
        var reservationA = reservedA.Data!["id"].S();
        var transfer = await api.Post($"/commercialcontracts/{a.Id}/stock-reservations/{reservationA}/transfer",
            new { targetContractId = c.Id, quantity = 4, reason = "أولوية التنفيذ" });
        Ok(transfer, "request transfer");
        var transferId = transfer.Data!["id"].S();
        Rejected(await DeliverAsync(api, c, product, 1), "pending transfer does not free stock");
        Rejected(await api.Post($"/stock-reservation-transfers/{transferId}/approve", new { }), "requester cannot approve", 403);

        var email = $"u{Guid.NewGuid():N}@test.com";
        Ok(await api.Post("/users", new { name = "معتمد", email, phone = "1", password = "Passw0rd!", role = "admin" }), "approver user");
        var approver = await api.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Ok(await approver.Post($"/stock-reservation-transfers/{transferId}/approve", new { note = "موافق" }), "approve transfer");
        Rejected(await approver.Post($"/stock-reservation-transfers/{transferId}/approve", new { }), "approve twice");

        var listC = (await api.Get($"/commercialcontracts/{c.Id}/stock-reservations")).Data!.AsArray();
        Assert.Equal(4m, listC.Sum(r => r!["remainingQuantity"].D()));
        Assert.Equal("transfer", listC[0]!["source"].S());
        var listA = (await api.Get($"/commercialcontracts/{a.Id}/stock-reservations")).Data!.AsArray();
        Assert.Equal(16m, listA.Sum(r => r!["remainingQuantity"].D()));
        Ok(await DeliverAsync(api, c, product, 4), "issue transferred allocation");
    }

    [Fact]
    public async Task Concurrent_reservations_never_exceed_available_stock()
    {
        var api = await NewConstructionTenantAsync();
        var product = await SeedProductAsync(api, "STL", stock: 10, cost: 50);
        var resource = await api.Post("/construction-resources", new { nameAr = "حديد", resourceType = "material", unit = "PCS", costSource = "product", productId = product });
        Ok(resource, "resource");
        var projects = new List<Project>();
        for (var n = 0; n < 4; n++) projects.Add(await ProjectAsync(api, resource.Data!["id"].G(), 10, "مشروع " + n));

        var results = await Task.WhenAll(projects.Select(p => ReserveAsync(api, p, product, 4)));
        Assert.Equal(2, results.Count(r => r.Status is 200 or 201)); // 10 ÷ 4 = حجزان فقط
        var avail = await AvailabilityAsync(api, projects[0], product);
        Assert.Equal(8m, avail["reserved"].D());
        Assert.Equal(2m, avail["available"].D());
    }

    [Fact]
    public async Task Project_members_are_employees_and_show_their_current_department()
    {
        var api = await NewTenantWithModulesAsync("accounting", "car_showroom", "pos", "construction", "hr");
        var dept = await api.Post("/departments", new { code = "ENG", nameAr = "الهندسة" });
        Ok(dept, "department");
        var deptId = dept.Data!["id"].G();
        var emp = await api.Post("/employees", new
        {
            code = "E1", nameAr = "م. سامي", departmentId = deptId, jobTitle = "مهندس موقع", hireDate = DateTime.UtcNow.Date.AddYears(-1),
            basicSalary = 5000, nationality = "SA",
        });
        Ok(emp, "employee");
        var employeeId = emp.Data!["id"].G();
        var product = await SeedProductAsync(api, "SND", stock: 0);
        var resource = await api.Post("/construction-resources", new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "product", productId = product });
        var p = await ProjectAsync(api, resource.Data!["id"].G(), 5, "مشروع الفريق");

        var candidates = (await api.Get($"/commercialcontracts/{p.Id}/project/member-candidates")).Data!.AsArray();
        var candidate = candidates.Single(x => x!["id"].G() == employeeId)!;
        Assert.Equal(deptId, candidate["departmentId"].G());

        Ok(await api.Post($"/commercialcontracts/{p.Id}/project/members", new { employeeId, role = "engineer" }), "add member");
        Rejected(await api.Post($"/commercialcontracts/{p.Id}/project/members", new { employeeId, role = "site_supervisor" }), "duplicate member", 409);

        // تعديل ملف الموظف وقسمه يظهر في عضوية المشروع دون تعديلها
        Ok(await api.Put($"/departments/{deptId}", new { code = "ENG", nameAr = "إدارة المشاريع" }), "rename department");
        var member = (await api.Get($"/commercialcontracts/{p.Id}/project")).Data!["members"]!.AsArray().Single()!;
        Assert.Equal(employeeId, member["employeeId"].G());
        Assert.Equal("إدارة المشاريع", member["departmentName"].S());
        Assert.Equal("م. سامي", member["userName"].S());

        // الموظف العضو لا يُحذف ملفه
        Rejected(await api.Delete($"/employees/{employeeId}"), "delete member employee", 409);
    }

    [Fact]
    public async Task Sales_and_general_transfers_cannot_consume_project_reservations()
    {
        var api = await NewConstructionTenantAsync();
        var product = await SeedProductAsync(api, "RSV", stock: 30, cost: 50);
        var resource = await api.Post("/construction-resources", new { nameAr = "حديد", resourceType = "material", unit = "PCS", costSource = "product", productId = product });
        Ok(resource, "resource");
        var p = await ProjectAsync(api, resource.Data!["id"].G(), 20, "مشروع محجوز");
        var reservation = await ReserveAsync(api, p, product, 20);
        Ok(reservation, "reserve");
        var main = (await api.Get("/warehouses")).Data!["items"]!.AsArray().First(x => x!["isDefault"]!.GetValue<bool>())!["id"].G();
        var branch = await api.Post("/warehouses", new { code = "BR1", nameAr = "فرع", nameEn = "Branch", location = "جدة", status = "active" });
        Assert.Equal(201, branch.Status);

        Task<Res> SellAsync(decimal qty) => api.Post("/sales-invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = qty, unitPrice = 100, vatRate = 15 } },
        });
        Task<Res> TransferAsync(decimal qty) => api.Post("/stocktransfers", new
        {
            fromWarehouseId = main, toWarehouseId = branch.Data!["id"].G(), items = new[] { new { itemId = product, quantity = qty } },
        });

        // المتاح غير المحجوز 30 − 20 = 10: بيع 11 مرفوض، وبيعان متزامنان بـ 6 ينجح أحدهما فقط
        Rejected(await SellAsync(11), "sale over unreserved", 409);
        var sales = await Task.WhenAll(SellAsync(6), SellAsync(6));
        Assert.Equal(1, sales.Count(r => r.Status is 200 or 201));
        Assert.All(sales.Where(r => r.Status is not (200 or 201)), r => Assert.Equal(409, r.Status));

        // الباقي غير المحجوز 4: التحويل العام لا ينقل المحجوز
        Rejected(await TransferAsync(5), "transfer reserved", 409);
        Ok(await TransferAsync(4), "transfer free");
        Rejected(await SellAsync(1), "sale with everything reserved", 409);

        // تحرير جزء من الحجز يعيده متاحاً للبيع والتحويل
        Ok(await api.Post($"/commercialcontracts/{p.Id}/stock-reservations/{reservation.Data!["id"].S()}/release", new { quantity = 5, reason = "فائض" }), "release");
        Ok(await SellAsync(2), "sale after release");
        Ok(await TransferAsync(3), "transfer after release");
        var avail = (await api.Get($"/commercialcontracts/{p.Id}/stock-availability?productIds={product}&warehouseId={main}")).Data!.AsArray().Single()!;
        Assert.Equal(15m, avail["onHand"].D());
        Assert.Equal(15m, avail["reserved"].D());
        Assert.Equal(0m, avail["available"].D());
    }
}
