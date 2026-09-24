using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.IntegrationTests.Infrastructure;
using Erp.SharedKernel.Tenancy;
using Erp.SharedKernel.Text;

namespace Erp.IntegrationTests;

[Collection(ErpCollection.Name)]
public sealed class PartiesAndPaymentsTests(ErpTestEnvironment env)
{
    private const string ValidIban = "SA0380000000608010167519";

    [Fact]
    public async Task Customer_gets_its_own_gl_account_and_an_opening_balance_that_follows_edits()
    {
        using var client = await env.SignInAsync(await env.ProvisionAsync(TenancyMode.Shared));

        var customer = await (await client.PostAsJsonAsync("/api/v1/customers", new { nameAr = "شركة اليمامة", phone = "0501234567", vatNumber = "310892837400003", openingBalance = 5000m })).ReadDataAsync();
        Assert.Equal("CUST-0001", customer.GetProperty("code").GetString());
        Assert.Equal("112001", customer.GetProperty("accountCode").GetString());
        Assert.Equal("corporate", customer.GetProperty("customerType").GetString());
        Assert.Equal(5000m, customer.GetProperty("currentBalance").GetDecimal());

        var id = customer.GetProperty("id").GetGuid();
        var updated = await (await client.PutAsJsonAsync($"/api/v1/customers/{id}", new { nameAr = "شركة اليمامة للمقاولات", phone = "0501234567", openingBalance = 3000m })).ReadDataAsync();
        Assert.Equal(3000m, updated.GetProperty("currentBalance").GetDecimal());

        var statement = await (await client.GetAsync($"/api/v1/customers/{id}/statement")).ReadDataAsync();
        Assert.Equal(3000m, statement.GetProperty("closingBalance").GetDecimal());

        var accounts = await (await client.GetAsync("/api/v1/accounts")).ReadDataAsync();
        var gl = accounts.EnumerateArray().Single(a => a.GetProperty("code").GetString() == "112001");
        Assert.Equal("شركة اليمامة للمقاولات", gl.GetProperty("nameAr").GetString());
        Assert.Equal("customer", gl.GetProperty("linkedEntityType").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/customers", new { nameAr = "خطأ", vatNumber = "12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/customers/{id}")).StatusCode);
    }

    [Fact]
    public async Task Supplier_and_bank_account_get_gl_accounts_with_the_right_nature()
    {
        using var client = await env.SignInAsync(await env.ProvisionAsync(TenancyMode.Shared));

        var supplier = await (await client.PostAsJsonAsync("/api/v1/suppliers", new { nameAr = "شركة عبد اللطيف جميل", bankName = "مصرف الراجحي", iban = ValidIban, openingBalance = 2000m })).ReadDataAsync();
        Assert.Equal("211001", supplier.GetProperty("accountCode").GetString());
        Assert.Equal(2000m, supplier.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(ValidIban, supplier.GetProperty("iban").GetString());

        var bank = await BankAccountAsync(client, 10000m);
        Assert.Equal("111001", bank.GetProperty("accountCode").GetString());
        Assert.Equal(10000m, bank.GetProperty("currentBalance").GetDecimal());
        Assert.Equal("RJHISARI", bank.GetProperty("swiftCode").GetString());

        var duplicate = await client.PostAsJsonAsync("/api/v1/bank-accounts", new { bankId = bank.GetProperty("bankId").GetGuid(), nameAr = "مكرر", accountNumber = "999", iban = ValidIban });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Opening balances land on account 33 and keep the books balanced.
        var trial = await (await client.GetAsync("/api/v1/accounting/reports/trial-balance")).ReadDataAsync();
        Assert.True(trial.GetProperty("isBalanced").GetBoolean());
    }

    [Fact]
    public async Task Receipt_voucher_posts_a_split_payment_and_cancelling_reverses_it()
    {
        using var client = await env.SignInAsync(await env.ProvisionAsync(TenancyMode.Shared));
        var customer = await (await client.PostAsJsonAsync("/api/v1/customers", new { nameAr = "خالد المنصور", phone = "0551112233", openingBalance = 5000m })).ReadDataAsync();
        var methods = await MethodsAsync(client);

        var voucher = await (await client.PostAsJsonAsync("/api/v1/vouchers", new
        {
            type = "receipt",
            partyType = "customer",
            partyId = customer.GetProperty("id").GetGuid(),
            payments = new object[]
            {
                new { paymentMethodId = methods["CASH"], amount = 1000m },
                new { paymentMethodId = methods["MADA"], amount = 500m, reference = "MADA-7781" },
            },
            notes = "دفعة من الحساب",
        })).ReadDataAsync();

        Assert.Equal($"RV-{DateTime.UtcNow.Year}-0001", voucher.GetProperty("voucherNumber").GetString());
        Assert.Equal(1500m, voucher.GetProperty("amount").GetDecimal());
        Assert.Equal("فقط ألف و خمسمائة ريالاً سعودياً لا غير", voucher.GetProperty("amountInWordsAr").GetString());
        Assert.True(voucher.GetProperty("isSplitPayment").GetBoolean());
        Assert.Equal("posted", voucher.GetProperty("status").GetString());

        var balances = await BalancesAsync(client);
        Assert.Equal(3500m, balances["112001"]);
        Assert.Equal(1000m, balances["1111"]);
        Assert.Equal(500m, balances["1112"]);

        var cancelled = await (await client.PostAsJsonAsync($"/api/v1/vouchers/{voucher.GetProperty("id").GetGuid()}/cancel", new { reason = "خطأ" })).ReadDataAsync();
        Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(5000m, (await BalancesAsync(client))["112001"]);
    }

    [Fact]
    public async Task Payment_voucher_through_a_bank_account_reduces_supplier_and_bank_balances()
    {
        using var client = await env.SignInAsync(await env.ProvisionAsync(TenancyMode.Shared));
        var supplier = await (await client.PostAsJsonAsync("/api/v1/suppliers", new { nameAr = "شركة الوعلان", openingBalance = 2000m })).ReadDataAsync();
        var bank = await BankAccountAsync(client, 10000m);
        var methods = await MethodsAsync(client);

        // A transfer without a bank account has nowhere to post.
        var rejected = await client.PostAsJsonAsync("/api/v1/vouchers", new
        {
            type = "payment",
            partyType = "supplier",
            partyId = supplier.GetProperty("id").GetGuid(),
            payments = new[] { new { paymentMethodId = methods["BANK_TRANSFER"], amount = 800m, reference = "TRX-1" } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var voucher = await (await client.PostAsJsonAsync("/api/v1/vouchers", new
        {
            type = "payment",
            partyType = "supplier",
            partyId = supplier.GetProperty("id").GetGuid(),
            payments = new[] { new { paymentMethodId = methods["BANK_TRANSFER"], amount = 800m, bankAccountId = bank.GetProperty("id").GetGuid(), reference = "TRX-1" } },
        })).ReadDataAsync();
        Assert.StartsWith($"PV-{DateTime.UtcNow.Year}-", voucher.GetProperty("voucherNumber").GetString(), StringComparison.Ordinal);

        var balances = await BalancesAsync(client);
        Assert.Equal(1200m, balances["211001"]);
        Assert.Equal(9200m, balances["111001"]);
    }

    [Fact]
    public void Amounts_are_written_in_arabic_words_like_the_frontend()
    {
        Assert.Equal("فقط ألف و خمسمائة ريالاً سعودياً لا غير", ArabicAmountInWords.Riyals(1500m));
        Assert.Equal("فقط ثلاثة آلاف ريالاً سعودياً و خمسون هللة لا غير", ArabicAmountInWords.Riyals(3000.50m));
        Assert.Equal("فقط مليونان و ثلاثمائة و خمسون ألف ريالاً سعودياً و خمسة و سبعون هللة لا غير", ArabicAmountInWords.Riyals(2_350_000.75m));
        Assert.Equal("صفر ريال سعودي لا غير", ArabicAmountInWords.Riyals(0));
    }

    private static async Task<JsonElement> BankAccountAsync(HttpClient client, decimal openingBalance)
    {
        var banks = await (await client.GetAsync("/api/v1/banks")).ReadDataAsync();
        var rajhi = banks.EnumerateArray().Single(b => b.GetProperty("code").GetString() == "RJHI").GetProperty("id").GetGuid();
        return await (await client.PostAsJsonAsync("/api/v1/bank-accounts", new
        {
            bankId = rajhi,
            nameAr = "الراجحي - الحساب الجاري",
            accountNumber = "608010167519",
            iban = ValidIban,
            openingBalance,
        })).ReadDataAsync();
    }

    private static async Task<Dictionary<string, Guid>> MethodsAsync(HttpClient client)
    {
        var methods = await (await client.GetAsync("/api/v1/payment-methods")).ReadDataAsync();
        return methods.EnumerateArray().ToDictionary(m => m.GetProperty("code").GetString()!, m => m.GetProperty("id").GetGuid());
    }

    private static async Task<Dictionary<string, decimal>> BalancesAsync(HttpClient client)
    {
        var accounts = await (await client.GetAsync("/api/v1/accounts")).ReadDataAsync();
        return accounts.EnumerateArray().ToDictionary(a => a.GetProperty("code").GetString()!, a => a.GetProperty("balance").GetDecimal());
    }
}
