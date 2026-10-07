using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>المخزون لكل مستودع: رصيد لكل مستودع بتكلفة موحّدة للصنف، والمستودع الافتراضي عند عدم التحديد.</summary>
[Collection("api")]
public class WarehouseStockTests : TestBase
{
    public WarehouseStockTests(ErpFactory factory) : base(factory) { }

    private static async Task<Guid> NewWarehouseAsync(Client api, string code)
    {
        var w = await api.Post("/warehouses", new { code, nameAr = "مستودع " + code, nameEn = code, location = "الرياض", status = "active" });
        Assert.Equal(201, w.Status);
        return w.Data!["id"].G();
    }

    private static async Task<decimal> OnHandAsync(Client api, Guid item, Guid warehouse)
        => (await api.Get($"/stockmovements/WarehouseStock?itemId={item}&warehouseId={warehouse}")).Data!.AsArray().Sum(r => r!["quantity"].D());

    private static async Task<Guid> DefaultWarehouseAsync(Client api)
        => (await api.Get("/warehouses")).Data!["items"]!.AsArray().First(w => w!["isDefault"]!.GetValue<bool>())!["id"].G();

    private static object Purchase(Guid item, decimal qty, decimal price, Guid? warehouse = null) => new
    {
        kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", status = "posted", warehouseId = warehouse,
        items = new[] { new { itemId = item, quantity = qty, unitPrice = price, vatRate = 15 } },
    };

    private static object Sale(Guid item, decimal qty, Guid? warehouse = null) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", warehouseId = warehouse,
        items = new[] { new { itemId = item, quantity = qty, unitPrice = 50, vatRate = 15 } },
    };

    [Fact]
    public async Task Documents_without_a_warehouse_use_the_default_and_each_warehouse_keeps_its_own_balance()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api);
        var main = await DefaultWarehouseAsync(api);
        var branch = await NewWarehouseAsync(api, "WH-B");
        var before = await OnHandAsync(api, item, main);

        var toDefault = await api.Post("/invoices", Purchase(item, 10, 20));
        Assert.Equal(201, toDefault.Status);
        Assert.Equal(main, toDefault.Data!["warehouseId"].G());
        Assert.Equal(201, (await api.Post("/invoices", Purchase(item, 4, 20, branch))).Status);

        Assert.Equal(before + 10, await OnHandAsync(api, item, main));
        Assert.Equal(4, await OnHandAsync(api, item, branch));

        // البيع من مستودع لا يصرف من رصيد مستودع آخر
        Assert.Equal(409, (await api.Post("/invoices", Sale(item, 5, branch))).Status);
        Assert.Equal(201, (await api.Post("/invoices", Sale(item, 3, branch))).Status);
        Assert.Equal(1, await OnHandAsync(api, item, branch));

        // مجموع المستودعات = رصيد الصنف
        var product = (await api.Get($"/products/{item}")).Data!;
        Assert.Equal(product["currentStock"].D(), await OnHandAsync(api, item, main) + await OnHandAsync(api, item, branch));
    }

    [Fact]
    public async Task Transfer_moves_quantity_between_warehouses_without_changing_total_stock_cost_or_the_ledger()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api);
        var main = await DefaultWarehouseAsync(api);
        var branch = await NewWarehouseAsync(api, "WH-T");
        Assert.Equal(201, (await api.Post("/invoices", Purchase(item, 10, 20))).Status);
        var product = (await api.Get($"/products/{item}")).Data!;
        var mainBefore = await OnHandAsync(api, item, main);
        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance"));

        object Transfer(Guid from, Guid to, decimal qty) => new { fromWarehouseId = from, toWarehouseId = to, items = new[] { new { itemId = item, quantity = qty } } };
        Assert.Equal(400, (await api.Post("/stocktransfers", Transfer(main, main, 1))).Status);
        Assert.Equal(409, (await api.Post("/stocktransfers", Transfer(branch, main, 1))).Status); // لا رصيد في المصدر
        var transfer = await api.Post("/stocktransfers", Transfer(main, branch, 6));
        Assert.Equal(200, transfer.Status);
        Assert.StartsWith("TRF-", transfer.Data!["transferNumber"].S());

        Assert.Equal(mainBefore - 6, await OnHandAsync(api, item, main));
        Assert.Equal(6, await OnHandAsync(api, item, branch));
        var after = (await api.Get($"/products/{item}")).Data!;
        Assert.Equal(product["currentStock"].D(), after["currentStock"].D());
        Assert.Equal(product["averageCost"].D(), after["averageCost"].D());
        Assert.Equal((debit, credit), Totals(await api.Get("/reports/TrialBalance")));

        // المرتجع يعود إلى مستودع فاتورته
        var sale = await api.Post("/invoices", Sale(item, 2, branch));
        Assert.Equal(201, sale.Status);
        Assert.Equal(200, (await api.Post("/invoices/returns", new { originalInvoiceId = sale.Data!["id"].S(), returnReason = "x" })).Status);
        Assert.Equal(6, await OnHandAsync(api, item, branch));
    }

    [Fact]
    public async Task Master_data_changes_are_audited_with_who_and_what_changed()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api);
        var product = (await api.Get($"/products/{item}")).Data!.AsObject();
        product["sellingPrice"] = 175;
        Assert.Equal(200, (await api.Put($"/products/{item}", product)).Status);

        var logs = (await api.Get($"/auditlogs?entityName=Product&entityId={item}")).Data!["items"]!.AsArray();
        Assert.Contains(logs, l => l!["action"].S() == "create");
        var update = Assert.Single(logs, l => l!["action"].S() == "update"); // حركات المخزون على الصنف ليست تعديلاً من مستخدم
        Assert.Contains("SellingPrice: 100 ← 175", update!["details"].S());
        Assert.False(string.IsNullOrWhiteSpace(update["performedBy"].S()));
    }

    [Fact]
    public async Task Item_reports_can_be_scoped_to_one_warehouse_where_transfers_appear()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api);
        var main = await DefaultWarehouseAsync(api);
        var branch = await NewWarehouseAsync(api, "WH-R");
        Assert.Equal(200, (await api.Post("/stocktransfers", new { fromWarehouseId = main, toWarehouseId = branch, items = new[] { new { itemId = item, quantity = 4 } } })).Status);

        // على مستوى المنشأة لا يظهر التحويل، وفي مستودعه يظهر وارداً
        var all = (await api.Get($"/reports/ItemLedger?itemId={item}")).Data!.AsArray();
        var inBranch = (await api.Get($"/reports/ItemLedger?itemId={item}&warehouseId={branch}")).Data!.AsArray();
        Assert.DoesNotContain(all, r => r!["docNumber"].S().StartsWith("TRF-"));
        var row = Assert.Single(inBranch);
        Assert.Equal(4, row!["quantityIn"].D()); Assert.Equal(4, row["runningStockBalance"].D());
        var audit = (await api.Get($"/reports/InventoryAudit?itemId={item}&warehouseId={branch}")).Data!.AsArray();
        Assert.Equal(4, Assert.Single(audit)!["systemQuantity"].D());
    }

    [Fact]
    public async Task Manual_adjustment_follows_its_warehouse_when_edited_or_deleted()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api);
        var main = await DefaultWarehouseAsync(api);
        var branch = await NewWarehouseAsync(api, "WH-A");
        var mainBefore = await OnHandAsync(api, item, main);

        object Adjust(decimal qty, Guid warehouse) => new { itemId = item, warehouseId = warehouse, type = "adjustment_in", quantity = qty, unitCost = 10, referenceNumber = "ADJ-1" };
        var adjustment = await api.Post("/stockmovements/adjust", Adjust(5, branch));
        Assert.Equal(200, adjustment.Status);
        Assert.Equal(5, await OnHandAsync(api, item, branch));

        // نقل التسوية إلى المستودع الرئيسي بكمية أخرى
        Assert.Equal(200, (await api.Put($"/stockmovements/{adjustment.Data!["id"].S()}", Adjust(3, main))).Status);
        Assert.Equal(0, await OnHandAsync(api, item, branch));
        Assert.Equal(mainBefore + 3, await OnHandAsync(api, item, main));

        Assert.Equal(200, (await api.Delete($"/stockmovements/{adjustment.Data["id"].S()}")).Status);
        Assert.Equal(mainBefore, await OnHandAsync(api, item, main));
    }
}
