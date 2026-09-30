using ERP.Tests.Infrastructure;

namespace ERP.Tests;

[Collection("api")]
public class InventoryCountTests : TestBase
{
    public InventoryCountTests(ErpFactory f) : base(f) { }

    private async Task<Client> UserAsync(Client owner, string role)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = role, email, phone = "1", password = "Passw0rd!", role })).Status);
        return await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
    }

    private static async Task<Res> ItemCountAsync(Client api, Guid product, decimal? counted)
        => await api.Post("/inventorycounts", new { scope = "items", countType = "full", countDate = DateTime.UtcNow, lines = new[] { new { itemId = product, countedQuantity = counted } } });

    private static async Task<Guid> VehicleAsync(Client api, string vin, string location, decimal cost)
    {
        var v = await api.Post("/vehicles", new { chassisNumber = vin, brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2025, purchasePrice = cost, sellingPrice = cost + 10000, vatMode = "standard_15", location });
        Assert.Equal(201, v.Status);
        return v.Data!["id"].G();
    }

    [Fact]
    public async Task Approved_item_count_adjusts_stock_posts_the_variance_journal_and_is_audited()
    {
        var api = await NewTenantAsync();
        var shortItem = await SeedProductAsync(api, "P1", stock: 10, cost: 40);
        var surplusItem = await SeedProductAsync(api, "P2", stock: 5, cost: 20);

        var created = await api.Post("/inventorycounts", new
        {
            scope = "items", countType = "cycle", countDate = DateTime.UtcNow,
            lines = new[] { new { itemId = shortItem, countedQuantity = (decimal?)7 }, new { itemId = surplusItem, countedQuantity = (decimal?)8 } },
        });
        Assert.Equal(201, created.Status);
        var count = created.Data!;
        Assert.Equal("draft", count["status"].S());
        Assert.StartsWith("IC-", count["countNumber"].S());
        Assert.Equal(120, count["totalShortageValue"].D()); // 3 × 40
        Assert.Equal(60, count["totalSurplusValue"].D());   // 3 × 20
        Assert.Equal(10, (await api.Get($"/products/{shortItem}")).Data!["currentStock"].D()); // لا أثر قبل الاعتماد

        var approval = await api.Post($"/inventorycounts/{count["id"].S()}/submit");
        Assert.Equal(200, approval.Status);
        Assert.Equal("pending", approval.Data!["status"].S());
        Assert.StartsWith("ICA-", approval.Data["approvalNumber"].S());
        Assert.Equal(409, (await api.Put($"/inventorycounts/{count["id"].S()}", new { scope = "items", countDate = DateTime.UtcNow, lines = new[] { new { itemId = shortItem, countedQuantity = 1 } } })).Status);

        var approved = await api.Post($"/inventorycountapprovals/{approval.Data["id"].S()}/approve", new { comment = "مطابق" });
        Assert.Equal(200, approved.Status);
        Assert.Equal("approved", approved.Data!["status"].S());
        Assert.Equal(7, (await api.Get($"/products/{shortItem}")).Data!["currentStock"].D());
        Assert.Equal(8, (await api.Get($"/products/{surplusItem}")).Data!["currentStock"].D());

        var journal = await api.Get($"/journalentries/{approved.Data["journalEntryId"].S()}");
        Assert.Equal(200, journal.Status);
        var lines = journal.Data!["lines"]!.AsArray();
        Assert.Equal(180, lines.Sum(l => l!["debit"].D()));
        Assert.Equal(lines.Sum(l => l!["debit"].D()), lines.Sum(l => l!["credit"].D()));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);

        var after = (await api.Get($"/inventorycounts/{count["id"].S()}")).Data!;
        Assert.Equal("approved", after["status"].S());
        Assert.Equal(approved.Data["journalEntryNumber"].S(), after["journalEntryNumber"].S());
        Assert.Equal(409, (await api.Post($"/inventorycountapprovals/{approval.Data["id"].S()}/approve", new { })).Status);
        Assert.Equal(409, (await api.Delete($"/inventorycounts/{count["id"].S()}")).Status);

        var audit = (await api.Get($"/auditlogs?entityName=InventoryCount&entityId={count["id"].S()}")).Data!["items"]!.AsArray().Select(a => a!["action"].S()).ToList();
        Assert.Contains("CREATED", audit); Assert.Contains("SUBMITTED", audit); Assert.Contains("ADJUSTED", audit);
    }

    [Fact]
    public async Task Journal_preview_matches_the_posted_entry_and_has_no_side_effects()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var count = await ItemCountAsync(api, product, 7);
        var approvalId = (await api.Post($"/inventorycounts/{count.Data!["id"].S()}/submit")).Data!["id"].S();
        var journalsBefore = (await api.Get("/journalentries")).Data!["totalCount"].D();

        var preview = (await api.Get($"/inventorycountapprovals/{approvalId}/journal")).Data!;
        Assert.True(preview["isPreview"]!.GetValue<bool>());
        Assert.True(preview["hasJournal"]!.GetValue<bool>());
        Assert.Null(preview["entryNumber"]);
        var previewLines = preview["lines"]!.AsArray().Select(l => (l!["accountCode"].S(), l["debit"].D(), l["credit"].D())).ToList();
        Assert.Equal(new[] { ("513", 120m, 0m), ("1141", 0m, 120m) }, previewLines);

        // المعاينة لا تترك أثراً: لا قيد ولا حركة مخزون ولا رقم مستهلك
        Assert.Equal(journalsBefore, (await api.Get("/journalentries")).Data!["totalCount"].D());
        Assert.Equal(10, (await api.Get($"/products/{product}")).Data!["currentStock"].D());

        await api.Post($"/inventorycountapprovals/{approvalId}/approve", new { });
        var posted = (await api.Get($"/inventorycountapprovals/{approvalId}/journal")).Data!;
        Assert.False(posted["isPreview"]!.GetValue<bool>());
        Assert.StartsWith("JE-", posted["entryNumber"].S());
        Assert.Equal(previewLines, posted["lines"]!.AsArray().Select(l => (l!["accountCode"].S(), l["debit"].D(), l["credit"].D())).ToList());
        Assert.Equal(journalsBefore + 1, (await api.Get("/journalentries")).Data!["totalCount"].D());
    }

    [Fact]
    public async Task Count_rules_are_validated_on_the_server()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        Assert.Equal(400, (await api.Post("/inventorycounts", new { scope = "items", countDate = DateTime.UtcNow, lines = Array.Empty<object>() })).Status);
        Assert.Equal(400, (await api.Post("/inventorycounts", new { scope = "items", countDate = DateTime.UtcNow, lines = new[] { new { itemId = product, countedQuantity = 1 }, new { itemId = product, countedQuantity = 2 } } })).Status);
        Assert.Equal(400, (await ItemCountAsync(api, product, -1)).Status);
        Assert.Equal(400, (await api.Post("/inventorycounts", new { scope = "vehicles", countDate = DateTime.UtcNow, lines = new[] { new { itemId = product, countedQuantity = 1 } } })).Status);
        Assert.Equal(400, (await api.Post("/inventorycounts", new { scope = "items", countDate = DateTime.UtcNow, lines = new[] { new { itemId = Guid.NewGuid(), countedQuantity = 1 } } })).Status);

        var draft = await ItemCountAsync(api, product, null); // حفظ جزئي مسموح
        Assert.Equal(201, draft.Status);
        Assert.Equal(0, draft.Data!["countedLines"]!.GetValue<int>());
        Assert.Equal(400, (await api.Post($"/inventorycounts/{draft.Data["id"].S()}/submit")).Status); // سطر لم يُعدّ

        var snapshot = await api.Post("/inventorycounts/snapshot", new { scope = "items" });
        Assert.Equal(10, snapshot.Data!.AsArray().Single()!["systemQuantity"].D());
        Assert.Equal(200, (await api.Delete($"/inventorycounts/{draft.Data["id"].S()}")).Status);
    }

    [Fact]
    public async Task Approval_enforces_permissions_and_segregation_of_duties_and_rejection_allows_resubmission()
    {
        var owner = await NewTenantAsync();
        var product = await SeedProductAsync(owner);
        var accountant = await UserAsync(owner, "chief_accountant");
        var rep = await UserAsync(owner, "sales_rep");

        var count = await ItemCountAsync(accountant, product, 9);
        var id = count.Data!["id"].S();
        var approval = (await accountant.Post($"/inventorycounts/{id}/submit")).Data!;
        var approvalId = approval["id"].S();

        Assert.Equal(403, (await rep.Post($"/inventorycountapprovals/{approvalId}/approve", new { })).Status);      // لا صلاحية اعتماد
        Assert.Equal(403, (await accountant.Post($"/inventorycountapprovals/{approvalId}/approve", new { })).Status); // لا يعتمد جرده بنفسه
        Assert.Equal(400, (await owner.Post($"/inventorycountapprovals/{approvalId}/reject", new { })).Status);      // السبب مطلوب
        Assert.Equal(200, (await owner.Post($"/inventorycountapprovals/{approvalId}/reject", new { comment = "أعد عدّ الرف" })).Status);

        var rejected = (await accountant.Get($"/inventorycounts/{id}")).Data!;
        Assert.Equal("rejected", rejected["status"].S());
        Assert.Equal("أعد عدّ الرف", rejected["rejectionReason"].S());
        Assert.Equal(10, (await owner.Get($"/products/{product}")).Data!["currentStock"].D());

        var line = rejected["lines"]![0]!;
        var edited = await accountant.Put($"/inventorycounts/{id}", new { scope = "items", countDate = DateTime.UtcNow, lines = new[] { new { id = line["id"].S(), itemId = product, countedQuantity = 10 } } });
        Assert.Equal(200, edited.Status);
        Assert.Equal("draft", edited.Data!["status"].S());
        Assert.Equal(0, edited.Data["varianceLines"]!.GetValue<int>());

        var second = (await accountant.Post($"/inventorycounts/{id}/submit")).Data!;
        Assert.NotEqual(approval["approvalNumber"].S(), second["approvalNumber"].S());
        Assert.Equal(200, (await accountant.Post($"/inventorycounts/{id}/withdraw")).Status);
        Assert.Equal("draft", (await owner.Get($"/inventorycounts/{id}")).Data!["status"].S());
        var history = (await owner.Get($"/inventorycounts/{id}/approvals")).Data!.AsArray().Select(a => a!["status"].S()).ToList();
        Assert.Equal(new[] { "withdrawn", "rejected" }, history);
    }

    [Fact]
    public async Task Multi_level_policy_gates_the_adjustment_until_the_last_level()
    {
        var owner = await NewTenantAsync();
        var product = await SeedProductAsync(owner);
        var accountant = await UserAsync(owner, "chief_accountant");
        var manager = await UserAsync(owner, "general_manager");
        Assert.Equal(201, (await owner.Post("/ApprovalPolicies", new
        {
            nameAr = "اعتماد فروق الجرد", documentType = "inventory_count", actionType = "approve", isActive = true,
            steps = new[] { new { level = 1, approverRole = "chief_accountant", titleAr = "رئيس الحسابات" }, new { level = 2, approverRole = "general_manager", titleAr = "المدير العام" } },
        })).Status);

        var count = await ItemCountAsync(owner, product, 4);
        var approval = (await owner.Post($"/inventorycounts/{count.Data!["id"].S()}/submit")).Data!;
        Assert.Equal(2, approval["totalLevels"]!.GetValue<int>());
        var approvalId = approval["id"].S();

        Assert.Equal(403, (await manager.Post($"/inventorycountapprovals/{approvalId}/approve", new { })).Status); // ليس دوره بعد
        var level1 = await accountant.Post($"/inventorycountapprovals/{approvalId}/approve", new { comment = "مستوى أول" });
        Assert.Equal("pending", level1.Data!["status"].S());
        Assert.Equal(2, level1.Data["currentLevel"]!.GetValue<int>());
        Assert.Equal(10, (await owner.Get($"/products/{product}")).Data!["currentStock"].D()); // لا تسوية قبل المستوى الأخير

        var final = await manager.Post($"/inventorycountapprovals/{approvalId}/approve", new { });
        Assert.Equal("approved", final.Data!["status"].S());
        Assert.Equal(4, (await owner.Get($"/products/{product}")).Data!["currentStock"].D());
    }

    [Fact]
    public async Task Vehicle_count_writes_off_missing_cars_and_reinstates_them_when_found()
    {
        var api = await NewTenantAsync();
        var present = Client.NewVin('1'); var missing = Client.NewVin('2'); var unknown = Client.NewVin('3');
        await VehicleAsync(api, present, "المعرض", 80000);
        var missingId = await VehicleAsync(api, missing, "المعرض", 60000);

        var snapshot = (await api.Post("/inventorycounts/snapshot", new { scope = "vehicles", location = "المعرض" })).Data!.AsArray();
        Assert.Equal(2, snapshot.Count);

        var count = await api.Post("/inventorycounts", new
        {
            scope = "vehicles", countDate = DateTime.UtcNow, location = "المعرض",
            lines = new[] { new { chassisNumber = present, countedQuantity = 1 }, new { chassisNumber = missing.ToLower(), countedQuantity = 0 }, new { chassisNumber = unknown, countedQuantity = 1 } },
        });
        Assert.Equal(201, count.Status);
        var lines = count.Data!["lines"]!.AsArray();
        Assert.Equal("vehicle_written_off", lines.Single(l => l!["chassisNumber"].S() == missing)!["resolution"].S());
        Assert.Equal("requires_registration", lines.Single(l => l!["chassisNumber"].S() == unknown)!["resolution"].S());
        Assert.Equal(60000, count.Data["totalShortageValue"].D());

        var approval = (await api.Post($"/inventorycounts/{count.Data["id"].S()}/submit")).Data!;
        var approved = await api.Post($"/inventorycountapprovals/{approval["id"].S()}/approve", new { });
        Assert.Equal(200, approved.Status);
        Assert.Equal("written_off", (await api.Get($"/vehicles/{missingId}")).Data!["status"].S());
        Assert.Equal(409, (await api.Put($"/vehicles/{missingId}", new { chassisNumber = missing, brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2025 })).Status);
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);

        var found = await api.Post("/inventorycounts", new { scope = "vehicles", countDate = DateTime.UtcNow, location = "الفرع", lines = new[] { new { chassisNumber = missing, countedQuantity = 1 } } });
        Assert.Equal("vehicle_reinstated", found.Data!["lines"]![0]!["resolution"].S());
        var second = (await api.Post($"/inventorycounts/{found.Data["id"].S()}/submit")).Data!;
        Assert.Equal(200, (await api.Post($"/inventorycountapprovals/{second["id"].S()}/approve", new { })).Status);
        var vehicle = (await api.Get($"/vehicles/{missingId}")).Data!;
        Assert.Equal("available", vehicle["status"].S());
        Assert.Equal("الفرع", vehicle["location"].S());
    }

    [Fact]
    public async Task Counts_are_isolated_per_tenant()
    {
        var a = await NewTenantAsync("أ");
        var b = await NewTenantAsync("ب");
        var count = await ItemCountAsync(a, await SeedProductAsync(a), 3);
        var approval = (await a.Post($"/inventorycounts/{count.Data!["id"].S()}/submit")).Data!;
        Assert.Equal(404, (await b.Get($"/inventorycounts/{count.Data["id"].S()}")).Status);
        Assert.Equal(404, (await b.Post($"/inventorycountapprovals/{approval["id"].S()}/approve", new { })).Status);
        Assert.Empty((await b.Get("/inventorycountapprovals")).Data!["items"]!.AsArray());
        Assert.Empty((await b.Get("/inventorycounts")).Data!["items"]!.AsArray());

        Assert.Single((await a.Get("/inventorycounts?scope=items&status=pending_approval")).Data!["items"]!.AsArray());
        Assert.Empty((await a.Get("/inventorycounts?scope=vehicles")).Data!["items"]!.AsArray());
        Assert.Single((await a.Get("/inventorycountapprovals?scope=items&status=pending")).Data!["items"]!.AsArray());
    }
}
