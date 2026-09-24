using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.IntegrationTests.Infrastructure;
using Erp.Modules.Inventory.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.IntegrationTests;

[Collection(ErpCollection.Name)]
public sealed class InventoryTests(ErpTestEnvironment env)
{
    [Fact]
    public async Task New_tenant_has_units_a_category_a_default_warehouse_and_a_costing_policy()
    {
        using var client = await env.SignInAsync(await env.ProvisionAsync(TenancyMode.Shared));

        var units = await (await client.GetAsync("/api/v1/units")).ReadDataAsync();
        Assert.Contains(units.EnumerateArray(), u => u.GetProperty("code").GetString() == "PCS");
        var warehouses = await (await client.GetAsync("/api/v1/warehouses")).ReadDataAsync();
        Assert.Single(warehouses.EnumerateArray(), w => w.GetProperty("isDefault").GetBoolean());
        var policy = await (await client.GetAsync("/api/v1/inventory/costing-policy")).ReadDataAsync();
        Assert.Equal("moving_average", policy.GetProperty("method").GetString());

        var fifo = await client.PutAsJsonAsync("/api/v1/inventory/costing-policy", new { method = "fifo", recalculateOnNewPurchase = true, includeFreightAndCustoms = true, negativeInventoryPolicy = "prohibit" });
        Assert.Equal(HttpStatusCode.BadRequest, fifo.StatusCode);
    }

    [Fact]
    public async Task Opening_stock_is_valued_in_the_ledger_and_moving_average_follows_receipts_issues_and_reversals()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        using var client = await env.SignInAsync(tenant);

        var product = await CreateProductAsync(client, openingQuantity: 10m, openingUnitCost: 60m);
        Assert.Equal("ITM-0001", product.GetProperty("sku").GetString());
        Assert.Equal(10m, product.GetProperty("currentStock").GetDecimal());
        Assert.Equal(60m, product.GetProperty("averageCost").GetDecimal());
        Assert.Equal(600m, (await BalancesAsync(client))["114"]);

        var productId = product.GetProperty("id").GetGuid();
        var warehouseId = await DefaultWarehouseAsync(client);
        var purchase = new StockDocument("purchasing", "purchase_invoice", Guid.NewGuid(), "PINV-1", DateOnly.FromDateTime(DateTime.UtcNow));
        var sale = new StockDocument("sales", "sales_invoice", Guid.NewGuid(), "INV-1", DateOnly.FromDateTime(DateTime.UtcNow));

        await InTenantAsync(tenant.Id, (inventory, ct) => inventory.ReceiveAsync(purchase, StockMovementType.InPurchase, [new StockLine(productId, warehouseId, 10m, 80m)], ct));
        var issued = await InTenantAsync(tenant.Id, (inventory, ct) => inventory.IssueAsync(sale, StockMovementType.OutSales, [new StockLine(productId, warehouseId, 5m)], ct));
        Assert.Equal(70m, issued[0].UnitCost);
        Assert.Equal(350m, issued[0].TotalCost);

        // Cancelling the purchase takes its 10 × 80 back out of the average: (15 × 70 − 10 × 80) / 5 = 50.
        await InTenantAsync(tenant.Id, async (inventory, ct) =>
        {
            await inventory.ReverseAsync(purchase, ct);
            return Array.Empty<StockLineResult>();
        });

        var after = await (await client.GetAsync($"/api/v1/products/{productId}")).ReadDataAsync();
        Assert.Equal(5m, after.GetProperty("currentStock").GetDecimal());
        Assert.Equal(50m, after.GetProperty("averageCost").GetDecimal());

        var tooMuch = await Assert.ThrowsAsync<ErpException>(() =>
            InTenantAsync(tenant.Id, (inventory, ct) => inventory.IssueAsync(sale with { DocumentId = Guid.NewGuid() }, StockMovementType.OutSales, [new StockLine(productId, warehouseId, 6m)], ct)));
        Assert.Equal("insufficient_stock", tooMuch.Code);
    }

    [Fact]
    public async Task Adjustments_post_to_the_ledger_transfers_keep_cost_and_recalculation_rebuilds_the_same_balances()
    {
        using var client = await env.SignInAsync(await env.ProvisionAsync(TenancyMode.Shared));
        var product = await CreateProductAsync(client, openingQuantity: 10m, openingUnitCost: 60m);
        var productId = product.GetProperty("id").GetGuid();

        var adjustment = await (await client.PostAsJsonAsync("/api/v1/stock-adjustments", new
        {
            reason = "تلف أثناء التخزين",
            lines = new[] { new { productId, quantity = -2m } },
        })).ReadDataAsync();
        Assert.Equal(120m, adjustment.GetProperty("valueOut").GetDecimal());
        var balances = await BalancesAsync(client);
        Assert.Equal(480m, balances["114"]);
        Assert.Equal(120m, balances["53"]);

        var second = await (await client.PostAsJsonAsync("/api/v1/warehouses", new { code = "WH-JED", nameAr = "مستودع جدة" })).ReadDataAsync();
        await (await client.PostAsJsonAsync("/api/v1/stock-transfers", new
        {
            fromWarehouseId = await DefaultWarehouseAsync(client),
            toWarehouseId = second.GetProperty("id").GetGuid(),
            lines = new[] { new { productId, quantity = 3m } },
        })).ReadDataAsync();

        var perWarehouse = await (await client.GetAsync($"/api/v1/stock-balances?productId={productId}")).ReadDataAsync();
        Assert.Equal(5m, perWarehouse.EnumerateArray().Single(b => b.GetProperty("warehouseCode").GetString() == "WH-MAIN").GetProperty("quantityOnHand").GetDecimal());
        var jeddah = perWarehouse.EnumerateArray().Single(b => b.GetProperty("warehouseCode").GetString() == "WH-JED");
        Assert.Equal(3m, jeddah.GetProperty("quantityOnHand").GetDecimal());
        Assert.Equal(60m, jeddah.GetProperty("averageCost").GetDecimal());
        Assert.Equal(480m, (await BalancesAsync(client))["114"]);

        await (await client.PostAsync("/api/v1/inventory/costing/recalculate", null)).ReadDataAsync();
        var rebuilt = await (await client.GetAsync($"/api/v1/products/{productId}")).ReadDataAsync();
        Assert.Equal(8m, rebuilt.GetProperty("currentStock").GetDecimal());
        Assert.Equal(60m, rebuilt.GetProperty("averageCost").GetDecimal());

        var ledger = await (await client.GetAsync($"/api/v1/stock-movements?productId={productId}")).ReadDataAsync();
        Assert.Equal(4, ledger.GetProperty("totalCount").GetInt32());
    }

    private async Task<IReadOnlyList<StockLineResult>> InTenantAsync(Guid tenantId, Func<IInventoryService, CancellationToken, Task<IReadOnlyList<StockLineResult>>> action)
    {
        await using var scope = await env.TenantScopeAsync(tenantId);
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        return await unitOfWork.ExecuteAsync(ct => action(scope.ServiceProvider.GetRequiredService<IInventoryService>(), ct));
    }

    private static async Task<JsonElement> CreateProductAsync(HttpClient client, decimal openingQuantity, decimal openingUnitCost) =>
        await (await client.PostAsJsonAsync("/api/v1/products", new
        {
            nameAr = "طقم إطارات بريدجستون",
            nameEn = "Bridgestone Tyre Set",
            barcode = $"628{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
            sellingPrice = 100m,
            minStockLevel = 2m,
            openingQuantity,
            openingUnitCost,
        })).ReadDataAsync();

    private static async Task<Guid> DefaultWarehouseAsync(HttpClient client)
    {
        var warehouses = await (await client.GetAsync("/api/v1/warehouses")).ReadDataAsync();
        return warehouses.EnumerateArray().Single(w => w.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
    }

    private static async Task<Dictionary<string, decimal>> BalancesAsync(HttpClient client)
    {
        var accounts = await (await client.GetAsync("/api/v1/accounts")).ReadDataAsync();
        return accounts.EnumerateArray().ToDictionary(a => a.GetProperty("code").GetString()!, a => a.GetProperty("balance").GetDecimal());
    }
}
