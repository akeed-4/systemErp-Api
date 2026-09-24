using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.IntegrationTests.Infrastructure;
using Erp.Modules.Accounting.Contracts;
using Erp.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.IntegrationTests;

[Collection(ErpCollection.Name)]
public sealed class AccountingTests(ErpTestEnvironment env)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task New_tenant_gets_the_default_chart_of_accounts_and_every_posting_mapping()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        using var client = await env.SignInAsync(tenant);

        var tree = await (await client.GetAsync("/api/v1/accounts?view=tree")).ReadDataAsync();
        Assert.Equal(["1", "2", "3", "4", "5"], tree.EnumerateArray().Select(a => a.GetProperty("code").GetString()!).ToList());

        var flat = await (await client.GetAsync("/api/v1/accounts")).ReadDataAsync();
        var receivables = flat.EnumerateArray().Single(a => a.GetProperty("code").GetString() == "112");
        Assert.Equal("asset", receivables.GetProperty("type").GetString());
        Assert.Equal("11", receivables.GetProperty("parentCode").GetString());

        var mappings = await (await client.GetAsync("/api/v1/accounting/posting-mappings")).ReadDataAsync();
        Assert.Equal(Enum.GetValues<PostingPurpose>().Length, mappings.GetArrayLength());
        Assert.Contains(mappings.EnumerateArray(), m => m.GetProperty("purpose").GetString() == "output_vat" && m.GetProperty("accountCode").GetString() == "212");
    }

    [Fact]
    public async Task Unbalanced_entries_and_postings_to_parent_accounts_are_rejected()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        using var client = await env.SignInAsync(tenant);

        var unbalanced = await PostEntryAsync(client, ("1111", 1000m, 0m), ("31", 0m, 900m));
        Assert.Equal(HttpStatusCode.BadRequest, unbalanced.StatusCode);

        var toParent = await PostEntryAsync(client, ("11", 1000m, 0m), ("31", 0m, 1000m));
        Assert.Equal(HttpStatusCode.BadRequest, toParent.StatusCode);
    }

    [Fact]
    public async Task Posted_entry_updates_balances_and_trial_balance_and_can_be_reversed_once()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        using var client = await env.SignInAsync(tenant);

        var entry = await (await PostEntryAsync(client, ("1111", 1000m, 0m), ("31", 0m, 1000m))).ReadDataAsync();
        Assert.Equal("posted", entry.GetProperty("status").GetString());
        Assert.Equal($"JE-{Today.Year}-0001", entry.GetProperty("entryNumber").GetString());

        var balances = await BalancesAsync(client);
        Assert.Equal(1000m, balances["1111"]);
        Assert.Equal(1000m, balances["111"]);
        Assert.Equal(1000m, balances["1"]);
        Assert.Equal(1000m, balances["31"]);

        var trial = await (await client.GetAsync("/api/v1/accounting/reports/trial-balance")).ReadDataAsync();
        Assert.True(trial.GetProperty("isBalanced").GetBoolean());
        Assert.Equal(1000m, trial.GetProperty("totalDebit").GetDecimal());

        var id = entry.GetProperty("id").GetGuid();
        var reversal = await (await client.PostAsJsonAsync($"/api/v1/journal-entries/{id}/reverse", new { reason = "خطأ إدخال" })).ReadDataAsync();
        Assert.Equal(id, reversal.GetProperty("reversalOfId").GetGuid());
        Assert.Equal(0m, (await BalancesAsync(client))["1111"]);

        var original = await (await client.GetAsync($"/api/v1/journal-entries/{id}")).ReadDataAsync();
        Assert.Equal("reversed", original.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/journal-entries/{id}/reverse", new { })).StatusCode);
    }

    [Fact]
    public async Task Posting_service_is_idempotent_per_source_and_kind_and_resolves_accounts_by_purpose()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        var source = new SourceRef("sales", "sales_invoice", Guid.NewGuid(), "INV-TEST-1");
        var request = new PostingRequest(source, "invoice", Today, "Sale", [
            new PostingLine(AccountRef.For(PostingPurpose.CashOnHand), 1150m, 0m),
            new PostingLine(AccountRef.For(PostingPurpose.SalesRevenue), 0m, 1000m),
            new PostingLine(AccountRef.For(PostingPurpose.OutputVat), 0m, 150m),
        ]);

        async Task<PostingResult> RunAsync(Func<IAccountingPostingService, Task<PostingResult>> action)
        {
            await using var scope = await env.TenantScopeAsync(tenant.Id);
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            return await unitOfWork.ExecuteAsync(_ => action(scope.ServiceProvider.GetRequiredService<IAccountingPostingService>()));
        }

        var first = await RunAsync(p => p.PostAsync(request, CancellationToken.None));
        var second = await RunAsync(p => p.PostAsync(request, CancellationToken.None));
        Assert.False(first.AlreadyPosted);
        Assert.True(second.AlreadyPosted);
        Assert.Equal(first.JournalEntryId, second.JournalEntryId);

        await RunAsync(p => p.ReverseAsync(source, "invoice", Today, "cancelled", CancellationToken.None));
        var reposted = await RunAsync(p => p.PostAsync(request, CancellationToken.None));
        Assert.False(reposted.AlreadyPosted);

        using var client = await env.SignInAsync(tenant);
        var vat = await (await client.GetAsync("/api/v1/accounting/reports/vat-return")).ReadDataAsync();
        Assert.Equal(150m, vat.GetProperty("outputVat").GetDecimal());
        var income = await (await client.GetAsync("/api/v1/accounting/reports/income-statement")).ReadDataAsync();
        Assert.Equal(1000m, income.GetProperty("netProfit").GetDecimal());
        var sheet = await (await client.GetAsync("/api/v1/accounting/reports/balance-sheet")).ReadDataAsync();
        Assert.True(sheet.GetProperty("isBalanced").GetBoolean());
    }

    [Fact]
    public async Task Closed_period_rejects_postings_until_reopened()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        using var client = await env.SignInAsync(tenant);

        var periods = await (await client.GetAsync($"/api/v1/fiscal-periods?year={Today.Year}")).ReadDataAsync();
        var current = periods.EnumerateArray().Single(p => p.GetProperty("month").GetInt32() == Today.Month).GetProperty("id").GetGuid();

        await (await client.PostAsync($"/api/v1/fiscal-periods/{current}/close", null)).ReadDataAsync();
        var rejected = await PostEntryAsync(client, ("1111", 10m, 0m), ("31", 0m, 10m));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("period_closed", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("problem").GetProperty("code").GetString());

        await (await client.PostAsync($"/api/v1/fiscal-periods/{current}/reopen", null)).ReadDataAsync();
        Assert.Equal(HttpStatusCode.Created, (await PostEntryAsync(client, ("1111", 10m, 0m), ("31", 0m, 10m))).StatusCode);
    }

    [Fact]
    public async Task Accounts_with_postings_cannot_get_sub_accounts_or_be_deleted()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        using var client = await env.SignInAsync(tenant);

        var created = await (await client.PostAsJsonAsync("/api/v1/accounts", new { code = "5701", nameAr = "إيجارات", parentCode = "57" })).ReadDataAsync();
        Assert.Equal(2 + 1, created.GetProperty("level").GetInt32());
        await (await PostEntryAsync(client, ("5701", 500m, 0m), ("1111", 0m, 500m))).ReadDataAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/accounts", new { code = "570101", nameAr = "فرعي", parentCode = "5701" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/accounts/{created.GetProperty("id").GetGuid()}")).StatusCode);

        var statement = await (await client.GetAsync($"/api/v1/accounts/{created.GetProperty("id").GetGuid()}/statement")).ReadDataAsync();
        Assert.Equal(500m, statement.GetProperty("closingBalance").GetDecimal());
    }

    [Fact]
    public async Task Party_sub_accounts_get_sequential_codes_under_their_control_account()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        await using var scope = await env.TenantScopeAsync(tenant.Id);
        var provisioning = scope.ServiceProvider.GetRequiredService<IAccountProvisioningService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var first = await provisioning.CreateSubAccountAsync(new SubAccountRequest(PostingPurpose.CustomerControl, "عميل 1", "Customer 1", "customer", Guid.NewGuid()), CancellationToken.None);
        var second = await provisioning.CreateSubAccountAsync(new SubAccountRequest(PostingPurpose.CustomerControl, "عميل 2", "Customer 2", "customer", Guid.NewGuid()), CancellationToken.None);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal("112001", first.Code);
        Assert.Equal("112002", second.Code);
        Assert.True(first.IsPostable);
        var parent = await scope.ServiceProvider.GetRequiredService<IAccountLookup>().FindByCodeAsync("112", CancellationToken.None);
        Assert.False(parent!.IsPostable);
    }

    private static Task<HttpResponseMessage> PostEntryAsync(HttpClient client, params (string Code, decimal Debit, decimal Credit)[] lines) =>
        client.PostAsJsonAsync("/api/v1/journal-entries", new
        {
            date = Today,
            description = "قيد اختبار",
            post = true,
            lines = lines.Select(l => new { accountCode = l.Code, debit = l.Debit, credit = l.Credit }),
        });

    private static async Task<Dictionary<string, decimal>> BalancesAsync(HttpClient client)
    {
        var accounts = await (await client.GetAsync("/api/v1/accounts")).ReadDataAsync();
        return accounts.EnumerateArray().ToDictionary(a => a.GetProperty("code").GetString()!, a => a.GetProperty("balance").GetDecimal());
    }
}
