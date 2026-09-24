using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.IntegrationTests.Infrastructure;
using Erp.Modules.EInvoicing.Contracts;
using Erp.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.IntegrationTests;

[Collection(ErpCollection.Name)]
public sealed class EInvoicingTests(ErpTestEnvironment env)
{
    private const string FirstPreviousHash = "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI0NjcyOWQ3M2EyN2ZiNTdlOQ==";

    [Fact]
    public async Task Invoices_are_chained_with_sequential_counters_and_carry_a_phase_1_qr_code()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        var firstSource = Guid.NewGuid();
        var first = await RegisterAsync(tenant.Id, "INV-1", 1150m, 150m, firstSource);
        var second = await RegisterAsync(tenant.Id, "INV-2", 575m, 75m);

        Assert.Equal(1, first.Icv);
        Assert.Equal(2, second.Icv);
        Assert.Equal(FirstPreviousHash, first.PreviousInvoiceHash);
        Assert.Equal(first.InvoiceHash, second.PreviousInvoiceHash);
        Assert.Equal(EInvoiceStatus.NotSubmitted, second.Status);

        var tags = DecodeTlv(first.QrCode);
        Assert.Equal($"شركة {tenant.Code}", tags[1]);
        Assert.Equal("300000000000003", tags[2]);
        Assert.Equal("1150.00", tags[4]);
        Assert.Equal("150.00", tags[5]);
        Assert.False(tags.ContainsKey(6));

        // Registering the same source again returns the same document instead of consuming a counter.
        var again = await RegisterAsync(tenant.Id, "INV-1", 1150m, 150m, firstSource);
        Assert.Equal(first.DocumentId, again.DocumentId);
        Assert.Equal(1, again.Icv);
    }

    [Fact]
    public async Task Configuration_hides_secrets_and_submission_is_refused_until_onboarding()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        using var client = await env.SignInAsync(tenant);

        var saved = await (await client.PutAsJsonAsync("/api/v1/zatca/config", new
        {
            environment = "simulation",
            csid = "TST-CSID-VALUE",
            privateKeyPem = "-----BEGIN EC PRIVATE KEY-----MHQCAQE-----END EC PRIVATE KEY-----",
        })).ReadDataAsync();
        Assert.Equal("simulation", saved.GetProperty("environment").GetString());
        Assert.True(saved.GetProperty("hasCsid").GetBoolean());
        Assert.True(saved.GetProperty("hasPrivateKey").GetBoolean());
        Assert.DoesNotContain("TST-CSID-VALUE", saved.ToString(), StringComparison.Ordinal);

        var registered = await RegisterAsync(tenant.Id, "INV-9", 115m, 15m);
        var documents = await (await client.GetAsync("/api/v1/zatca/documents")).ReadDataAsync();
        Assert.Equal(1, documents.GetProperty("totalCount").GetInt32());

        var submit = await client.PostAsync($"/api/v1/zatca/documents/{registered.DocumentId}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        Assert.Equal("zatca_onboarding_required", (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("problem").GetProperty("code").GetString());
    }

    private async Task<EInvoiceResult> RegisterAsync(Guid tenantId, string number, decimal total, decimal vat, Guid? sourceId = null)
    {
        var id = sourceId ?? Guid.NewGuid();
        await using var scope = await env.TenantScopeAsync(tenantId);
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        return await unitOfWork.ExecuteAsync(ct => scope.ServiceProvider.GetRequiredService<IEInvoicingService>().RegisterAsync(
            new EInvoiceRequest("sales", "sales_invoice", id, number, EInvoiceKind.SimplifiedTaxInvoice, DateTimeOffset.UtcNow, total, vat),
            ct));
    }

    private static Dictionary<int, string> DecodeTlv(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var tags = new Dictionary<int, string>();
        for (var i = 0; i + 1 < bytes.Length; i += 2 + bytes[i + 1])
        {
            tags[bytes[i]] = Encoding.UTF8.GetString(bytes, i + 2, bytes[i + 1]);
        }

        return tags;
    }
}
